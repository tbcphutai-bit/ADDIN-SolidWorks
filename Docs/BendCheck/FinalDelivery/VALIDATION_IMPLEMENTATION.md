# Executed final-delivery validation — 2026-10-20

**Verdict: NOT ACCEPTED for production / keep Draft.** The implementation, input workflow and functional tests are delivered. Real-company mounting/process evidence is absent, and native clean shutdown still fails its original gate. No manufacturing PASS, approved company datum or replacement CAD was generated.

## Source and architecture

- Implementation branch: `feature/check-chan-final-evidence-delivery`; PR target: `feature/check-chan-native-excel-4200-6000` (PR #35). No existing PR or main was merged/modified.
- PR #35 source baseline: `02cbe20fd8713734d1011ede22928f15025a87a8`.
- All seven original handoff files at `53d963a11eb4b7c5a8c406ca4be8211248ed5f88` were read against the compiled source. The subsequent wording addendum at `4bb2e40` was also read and implemented, without changing the pinned engine baseline.
- Engine/native identity implementation checkpoint: `b58b785c6e415e2b67400dd2b2995f061c1d22de`.
- Vietnamese UX/native-smoke checkpoint: `808ca7d4a69d70c58d90a43a6dcbe364f91638d0`. Later source changes only add accurate positive numeric-reason wording and a presentation regression; report commits do not change geometry algorithms.
- [Chosen architecture and alternatives](IMPLEMENTED_ARCHITECTURE.md): reuse the fixed company catalog and existing material/datum/seating/axial/process verifiers. Add a private unsigned review workspace and a separately verified final-static provider. Native acquisition remains on STA; numeric/import workers receive managed DTOs.
- [Exact input workflow](REVIEW_INPUT_WORKFLOW.md): role assignments, datums, seating, finite useful spans, authority pins, native registration, revisions, artifacts and process graph. The reference mutable Boolean `FinalEvidenceGate` was deliberately replaced by projection of actual guarded certificates, not copied as an authority.

## Actual company library — G0 / G1

Master source SHA256: `e3f34e62d088e74aedde1c54694428c1faca0504aa6176ea89ebf777bda4b016`. Original Excel SHA256: `afbb2e6245031604ddbb83738a7eae1ec9c1948dcd1128d61ae25d7213f962df`. Both original files remained unchanged.

**18/18 fixed pairs matched** their existing source selection. The actual-pair regression passed **16,977 assertions**; the new complete Holder inventory passed **19 assertions**. No nearest-neighbor pairing or geometry rewrite was added.

| Observation | Actual count |
|---|---:|
| Holder inventories classified by topology | 18/18 |
| Source Holder entities retained in private unsigned proposals | 5,572 |
| Supported / total SPLINE entities | 160/160 |
| Holders with 347 entities / 32 connected groups / 4 closed-simple groups / 28 open-or-branched groups | 16 |
| ATC Holders with 10 entities / 8 groups / 2 closed-simple / 6 open-or-branched | 2 |
| Valid closed Punch regions | 17 |
| KIK-7181328001 Punch | OPEN, unchanged |
| Engineering material-role review completed from available evidence | **0/18** |
| Physical clamp approval | **0/18** |

[Full 18-tool status table](ImplementationResults/holder-status.md). Every tool remains `MATERIAL_ROLE_PENDING`; `ClampVerified=false`. Connectivity/closedness does not identify which edges are real material, holes or drawing references. No source entity was silently excluded. The application provides explicit assignments and validation; only an independent source-bound review can turn them into production material.

## Build and managed / numeric execution

Windows x64, .NET Framework 4.8, C# 7.3; MSBuild 18.10. Product **Debug/x64 PASS**, **Release/x64 PASS**. Managed, UI and native harness builds also passed. Builds used private output directories with deployment, COM registration, pre/post build actions and SOLIDWORKS termination disabled.

| Executed test | Result | Actual count / assertion evidence |
|---|---|---|
| Full managed Debug | PASS | 318 tests, 0 failures, 9,175 assertions |
| Full managed Release | PASS | 318 tests, 0 failures, 9,175 assertions |
| New review/native-identity/final-static cases, each configuration | PASS | 16 tests, 63 assertions; included in full totals |
| Assembly-only | PASS | 20 tests, 222 assertions; included in full totals |
| Planning draft, each configuration | PASS | 24 tests, 609 assertions |
| Actual CAD pair source | PASS | 18/18, 16,977 assertions |
| Actual Holder inventory | PASS | 18/18, 19 assertions |
| Independent GEOS/Shapely 2D oracle | PASS | 552 cases; false CLEAR=0, false COLLISION=0 |
| Independent axial oracle | PASS | 426 cases; false CLEAR=0, false COLLISION=0 |
| Independent native-family analytic oracle | PASS | 480 cases; false CLEAR=0, false COLLISION=0 |
| Independent continuous-motion oracle | PASS | 182 cases; errors=0; sparse three-frame sampling misses 103 collisions |
| Full numeric reference vs archived baseline | PASS | 1,620 exact-equal records; 13,932 actual queries, 2,268 intervals, 540 refinements |
| Controlled numeric 18×3 / 18×6 / 120-instance–25-source | PASS | Independent/cold/warm statuses, reasons, witnesses, contact and error bounds equal; 4 assertions per benchmark |

The new final-static importer test actually executes the existing numeric engine, retains 36 candidate rows for two bends, verifies the reviewed **controlled synthetic** pair, and withdraws held results when an artifact changes. It does not sign any company input. Existing numeric oracles and benchmarks are controlled software fixtures, not physical setups for the company CAD pairs.

One earlier full Debug run failed the existing sequence-vs-independent comparison (317 PASS / 1 FAIL / 9,172 assertions) during overlapping validation. No budget was changed. A diagnostic was added, the sequence-only run passed 11/11 (45 assertions), and the final isolated full run passed 318/318. The original failure is retained privately; its timing-dependent cause was not proved, so it is not reported as a separately fixed product bug.

## Numeric performance — controlled fixtures only

Times are this machine's measured wall times, not a new performance guarantee. Phase solver budgets/tolerances are unchanged. A single-source cold batch can cost more than independent execution; the implementation does not claim otherwise.

| Fixture | Independent ms / queries | Cold ms / queries | Warm ms / new queries | Peak cold workers | Cold cache / peak managed bytes |
|---|---:|---:|---:|---:|---:|
| 18×3 | 1,090.452 / 4,842 | 1,108.647 / 4,842 | 4.439 / 0 | 1 | 2,370,544 / 11,761,096 |
| 18×6 | 5,519.294 / 9,684 | 6,068.123 / 9,684 | 20.215 / 0 | 1 | 4,724,320 / 15,763,296 |
| 120 instances / 25 unique controlled sources | 54,124.762 / 141,840 | 3,750.795 / 29,550 | 121.606 / 0 | 4 | 17,812,848 / 72,134,144 |

The 120/25 cold run had 25 geometry/static/process misses. Warm static/process hits were 25/25; geometry bookkeeping was 3 hits / 22 misses while reusing valid numeric certificates. The independently approved PartLocal scope in this synthetic benchmark allows sharing; the new final-static importer does not borrow a narrower process-only approval for broader static input.

## Original 33 native Parts and Check Min

The **original saved 33 fixtures** from PR #35 were opened read-only, not regenerated. Sidecar SHA256 `dba85de9ecc3001f62b7ee3dd0536841fa16bb3a2c2e98e806b11a2de861f5a4`. SOLIDWORKS 2024 SP5, revision `32.5.0`.

At the engine checkpoint after the native identity fix:

| Actual result | Debug | Release |
|---|---:|---:|
| Functional cases PASS / FAIL / NOT RUN | 33 / 0 / 0 | 33 / 0 / 0 |
| Assertions | 3,879 | 3,879 |
| Native bends | 72 | 72 |
| Static candidate rows / independent Punch+Holder evaluations | 1,296 / 2,592 | 1,296 / 2,592 |
| Process candidate rows / phase-pair rows | 1,296 / 12,960 | 1,296 / 12,960 |
| Check Min MeetsMinimum / BelowMinimum | 69 / 3 | 69 / 3 |
| Native geometry and full cold output exactly equal to PR #35 | YES | YES |
| Cold captures / warm captures / warm capture hits | 33 / 0 / 33 | 33 / 0 / 33 |
| Real-company numeric queries | **0** | **0** |
| Source bytes protected / worker COM captures | YES / 0 | YES / 0 |
| Mean cold / warm run-to-visible ms | 2,759.045 / 835.263 | 3,271.746 / 877.026 |
| Original 10-second shutdown gate | NOT_CONFIRMED | NOT_CONFIRMED |
| **Overall native release gate** | **FAIL** | **FAIL** |

The assertions differ from PR #35's generator runs because this replay reuses saved Parts and does not assert a second generation. It still verifies native dimensions, actual face ownership/FeatureIds, Min, cold/warm, first-case rebuild/refresh, native UI navigation and source protection.

Full **33/33** per-case reports: [Debug](ImplementationResults/native-debug/native-cases.md), [Release](ImplementationResults/native-release/native-cases.md); each linked CSV includes lengths, W/L/t, native FeatureIds, Min, 18/bend, separate Punch/Holder/process outcomes, reasons and timings. [Machine-readable baseline comparison](ImplementationResults/native-baseline-comparison.json). These are engine-checkpoint results, not a claim that every later presentation-only edit reran all 66 opens.

All 594 planner tool decisions per configuration remain `NeedsEvidence`. No winner or common production tool is emitted. All actual static Punch/Holder and process outcomes remain UNKNOWN. Reference lengths 4,200/6,000 mm are never treated as certified working tool spans.

### XL-17 walkthrough

Actual AL 3 mm U fixture: bend-axis length **6,000 mm**, W=**50 mm**, L=**100 mm**, native FeatureIds **53, 59**. Both Min results meet the existing limits: C/C **50 / 18 mm**, L/A **100 / 14.9 mm**. Each bend retains 18 static and 18 process candidates.

Static reason: `TOOL_AXIAL_EXTENT_UNVERIFIED`; UI explanation: **Thiếu chiều dài làm việc của dao**. The Holder review pane separately identifies unreviewed material roles and seating. The process provider has no reviewed intermediate sheet graph/kinematics. Hence no company numeric queries, no route winner, manufacturing `CHUA_XAC_MINH`.

At the Vietnamese UX checkpoint, this same original XL-17 was replayed in **Debug and Release: 1/1 functional PASS, 131 assertions each**, rebuild/refresh and source protection PASS, zero worker COM captures; shutdown NOT_CONFIRMED, overall FAIL. These single-case smoke records were **not merged into** the 33-case series to replace failures/retries.

The genuine NG remain **XL-19 / XL-20 / XL-21, FeatureId 53, ST 3.2 mm, A=15 mm < 15.2 mm**. Neither UI localization nor review import changes this.

## Native Part / Assembly / lifecycle

| Run | Functional result | Lifecycle / limitation |
|---|---|---|
| Release native Part 0/3/6 with actual existing controller/form | PASS, 219 assertions | Shutdown NOT_CONFIRMED; overall FAIL |
| Later Debug native Part 0/3/6 | **FAIL**, 113 assertions | 0/3 Part checks passed; six-bend `OpenDoc6` raised `0x80010105 RPC_E_SERVERFAULT`, six-bend check NOT RUN; shutdown NOT_CONFIRMED |
| Native Assembly 120 components / 25 source-config groups | PASS, 291 assertions (173 core) | Shutdown NOT_CONFIRMED; overall FAIL |
| Selected 10 / selected subassembly 70 / Entire 120 | PASS | Actual valid occurrence captures executed, not just eligibility flags |
| Normal + reflected / Assembly Cut / flexible ancestry / suppressed | PASS in native mixed fixture | Normal siblings still execute; blocked siblings remain independently UNKNOWN |
| Standalone lightweight probe | PASS, state restored, update stamp/dirty flag unchanged | Backend resolve count 0 |
| Lightweight inside flexible fixture | **NOT RUN** | Requested state was not achieved (native state 2); no flag fabricated |
| Native opposite-hand mirror feature | **NOT RUN** | SOLIDWORKS `CreateFeature` returned null; reflected-transform case did run |
| Original fixtures / six-bend seed / CAD / Excel changed | NO | Source hashes preserved; six-bend seed SHA remains `126a7a288bab23a18389157ca4da405b1163ec5e10c8f7b12106d4bfef3006e2` |

Actual 120/25 native cold: **58,367.927 ms**, STA capture **49,851.772 ms**, 25 captures / 95 geometry hits, peak 4 workers / 91,609,224 managed bytes. Warm: **5,713.852 ms**, 0 captures / 25 capture hits / 120 geometry hits, 184,058,504 peak managed bytes. Selected 10: **7,500.015 ms**, four native captures. Every run had **0 numeric queries** because physical setup/process inputs are missing. This measures acquisition/cache responsiveness, **not numeric collision acceleration**. Independent per-instance worker-only baseline was 630.753 ms and excludes native capture; it is not a comparable end-to-end speedup denominator.

### Root causes found, fixes, and remaining crash

1. **Captured `KeyNotFoundException`, `BendPanelGeometry.cs:88`:** a face traversal edge was looked up solely by IUnknown address. The old numeric pointer dictionary also failed to retain the source wrapper array. `NativeEntityReferenceIndex<T>` retains originals and resolves only native persistent references or exact `IsSame` equality; never coordinates, skipped entities, or a recycled transient alias cache. It is also used by optional B-rep capture. This is a native identity fix, **not a Check Min mathematics change**. Managed regressions cover alias/recycled/missing/ambiguous identities. Subsequent complete 33-case Debug and Release runs passed functionally. Observed counters in these replays were zero persistent/kernel fallbacks; retaining source wrappers is relevant, but the specific fallback path is not falsely claimed to have executed natively.
2. **Background-session lifetime hazard:** harness use of `UserControl=false` around last-document close was replaced by `UserControlBackground=true` on the exact owned PID. The isolated lastdoc A/B both reopened successfully, so this documented hazard is not asserted to explain all RPC failures. Startup failures now record owned PID, readiness/ROT/exit details; no fallback to another session. The replay script also compares UTC ticks, avoiding PowerShell JSON date-coercion bypass of its owned-process safety wait.
3. **Shutdown `0xc0000374` remains unresolved.** Private WinDbg analysis of the actual owned dump reports `HEAP_CORRUPTION_ACTIONABLE_BlockNotBusy_DOUBLE_FREE_c0000374_ucrtbase.dll!free_base`, `sldappu!swTaskpaneView_c::getAuDispatch`, `clr!RCW::ReleaseAllInterfaces`, COM shutdown and `CAmApp::ExitInstance`. The identified RCW interface is `SolidWorks.Interop.sldworks.ICommandManager`. This repository does not acquire that interface; other installed add-ins are present in the dump. That is evidence for further process-wide lifetime isolation, **not proof assigning blame to another product**. No speculative vendor/Taskpane release changes were made.
4. **The later six-bend OpenDoc6 server fault remains reproducible in one run**, despite the identity/background controls. It is not hidden by the successful 33-case runs. After preserving its failed report, the exact owned failed PID was cleaned up using matching PID + creation UTC ticks; the result remains FAIL. No user's session was terminated.

### Actual binary A/B control

Both controls unloaded only the installed path in their **owned session**, loaded the requested archived/new DLL, recorded its mapped module path/SHA, opened the **same original XL-01** read-only (22 native features), closed it, and checked the unchanged bytes. No registry/deployment/installation was modified. The 10-second exit criterion was not raised.

| Control | Owned PID | Observed requested module SHA256 | Functional open/close | Shutdown |
|---|---:|---|---|---|
| Archived PR #35 build artifact | 55600 | `78a8d5c9a943802dbfb08699741e2e05d16d7dc76444b12c5783820235cdd0d3` | PASS | NOT_CONFIRMED |
| Implementation build before wording-only follow-up | 69248 | `e8b4f1d7fb225775613958c3757830d87ee567b8b8e863f8e797c305d40e84b3` | PASS | NOT_CONFIRMED |

`RequestedModuleObserved=true` in both. `GetAddInObject` returned null; **in-process facade/command identity was not established**. The native-backed Check Chấn form tests instantiate the built production controller/backend in an STA harness and operate real SOLIDWORKS COM, rather than proving a click inside the installed in-process command. A fully isolated in-process command/disconnect A/B is therefore **NOT RUN**. These distinctions prevent a reflected out-of-process assembly or a load return code being advertised as loaded-command proof. Raw dump contents, module machine paths, original CAD screenshots and private review payloads remain local.

Earlier failures are retained: initial Release 32/33 (XL-01), initial Debug 13/33 with later RPC errors, a startup-only 33 NOT RUN attempt, and pre-fix Release XL-12's captured KeyNotFound. The complete post-fix matrix is a new run, not a splice of successful retries. The current remaining shutdown/server faults keep G6 FAIL.

## Vietnamese UX and screenshot evidence

`BendCheckDisplayText` has **9 status/Min/sequence entries, 5 phase entries and 28 explicit reason entries**, in addition to translated form captions, filters, headers, tooltips and progress. The same form uses **Bắt đầu kiểm tra**, **Toàn bộ cụm lắp ráp**, **Các chi tiết đã chọn**, **Chày chấn**, **Đầu kẹp chày**, **Chưa đủ dữ liệu**, and **Chi tiết kỹ thuật** for exact code recovery. Lower V-die is explicitly excluded. Display colors come from underlying enums, not translated strings. Unknown reason codes remain safely unexplained/technical; they never become success.

- 19 WinForms test groups in each configuration, no failures; exact final assertion counts are in the execution metrics file. Includes full raw recovery, missing-vs-zero clearance, no fake pass at zero queries, stale withdrawal, source badge/scope caption fit and existing identity/cancel/refresh/navigation regressions.
- Rendered actual WinForms with requested client sizes 1024×680, 1264×821, 1920×1080 and programmatic scale factors 100/125/150%; measured device DPI **96**. The OS/work-area limit clamps some larger/scaled windows to height 1065; the run records actual client bounds. **Actual OS DPI 125/150%: NOT RUN.** Scale tests and requested dimensions are not claimed as physical Windows DPI or unclamped viewport testing.
- [Before, controlled fixture](ImplementationResults/ui/before-1024.png), [after, same controlled fixture](ImplementationResults/ui/after-1024.png), [numeric controlled simulation](ImplementationResults/ui/simulation-sequence.png). These images contain synthetic geometry only. Company CAD review screenshots remain private.
- Technical input JSON, actual ToolIds/native names, handles, source versions and expert codes are intentionally retained in technical views. No global log replacement, backend serialization localization or translated proof hash was introduced. Legacy Check Min form/command is unchanged; only the existing unified form's projection captions were clarified.

## Acceptance gates and required engineering input

| Gate | Result | Explanation |
|---|---|---|
| G0 fixed source | PASS | 18/18 exact CAD pair selections, unchanged hash |
| G1 Holder material | Inventory PASS / actual engineering review BLOCKED | 18 explicit inventories; 0 reviewed roles; no omitted entities |
| G2 physical mount/axial | BLOCKED | No independently approved tip/mount datum, physical seating, finite working spans or setup errors |
| G3 process/registration | BLOCKED | No reviewed whole-sheet intermediate states, kinematics and complete graph for the 33 Parts |
| G4 numeric software | PASS in controlled scope; real-company numeric NOT RUN (0 queries) | Independent oracles/reference/benchmarks execute; missing evidence is not bypassed |
| G5 planner software | PASS in controlled scope; real-company route UNKNOWN | Prefers one common pair with zero changes when a complete proven path exists; no real winner |
| G6 native release | **FAIL** | Shutdown NOT_CONFIRMED and later OpenDoc6 server fault; fully isolated in-process command A/B missing |
| G7 UX / release | Functional UX PASS with stated DPI/host limits; release NOT ACCEPTED | UNKNOWN and simulation visible; production remains unverified |

No additional CAD drawing or another 33-case series is requested. The company needs to review the preserved Holder entities' solid/hole/island/reference roles, measured tip/mount and Punch–Holder seating, useful axial intervals and uncertainty, machine/setup constraints, and registered before/after whole-sheet states plus five-phase motion/complete sequence alternatives. These must be externally approved with revision, authority and artifact binding. A CAD reference label or typed coordinate is not that approval.

Next native investigation needs an isolated supported SOLIDWORKS host/add-in set and a reproducible fully loaded in-process command/disconnect trace, correlated to the observed ICommandManager cleanup. Until that and G2/G3 evidence are resolved, this delivery can be reviewed/tested as software but **cannot pass overall production acceptance**.

## Files / reproducibility

[Complete changed-file list](ImplementationResults/changed-files.md) and [sanitized measured metrics](ImplementationResults/execution-metrics.json). New production classes are `HolderMaterialReviewDraftBuilder`, `ToolEvidenceReviewWorkspace`, `FinalStaticSetupImport` / `ProcessProgramProvider` final-static partial, `NativeEntityReferenceIndex<T>`, `BendToolEvidenceControl`, and `BendCheckDisplayText`. Existing placement readers, source leases, managed snapshot projection and Part/Assembly orchestration are extended around the original solvers.

Reproduce with the existing managed/UI/Planning projects, `ToolProfiles.Tests.exe --final-review-tests`, `--cad-pairs-company`, `--final-review-company`, `--process-reference`, `--assembly-benchmark`, existing independent Python oracles, `Run-ExistingNativeMatrix.ps1` with the original private sidecar/report, and native `--part-ui` / Assembly / `--shutdown-audit` harnesses. `report_final_evidence_delivery.py` rejects a different or duplicate 33-case specification and produces the sanitized baseline comparison; `report_native_excel_matrix.py --public` emits per-case tables without machine paths. Detailed signed records, source coordinates, CAD/Excel, raw logs/dumps and trust keys are **not** in this PR.
