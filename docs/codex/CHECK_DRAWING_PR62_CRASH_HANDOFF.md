# CODEX HANDOFF — CHECK DRAWING PR #62: current architecture, native crashes, evidence & acceptance

**Snapshot reviewed:** 2026-11-18. **Source HEAD before this docs handoff:** `838c99753a4af101520b4ec2b16863ba9323e409`. Latest **functional** CHECK DRAWING change documented by Anti: `2eae32668bdcb18421abc36ed7f150d0b7756703`. Later commits `92cebdc`, `061f635`, `838c997` update the technical report rather than production C#.
**Repository:** https://github.com/tbcphutai-bit/ADDIN-SolidWorks
**Draft PR:** https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/62
**Branch:** `fix/check-drawing-goban-display-retry-uraomote-20261108`
**Reference report:** `docs/reports/CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md` (includes crash-triage, native minidumps and A/B isolated worker trials).
**Target:** SOLIDWORKS 2024 SP5.0, Windows, C# / COM interop, Debug|x64, legacy explicit-include ADDIN.csproj.

## 1. User's intent and required outcome

The user implemented CHECK DRAWING to compare technical Drawing to BOM/Part, and needs it **accurate, safe and stable in daily SOLIDWORKS usage**. Current issue: native SW crashes / Windows EV1000 in test hosts, and misleading WARNING for otherwise correctly marked `表/ウラ` on Flat Pattern Drawing. They want **Codex to independently assess all evidence and design one consolidated final resolution**, NOT another chain of speculative small patches.

**Critical distinction:** It is not proved that CHECK DRAWING itself causes the `0xC0000374` exit heap corruption. A no-Drawing, no-ADDIN.dll-loaded SOLIDWORKS test also reproduces it when test harness calls `swApp.ExitApp()`. Conversely, another `sldapiu.dll 0xC0000005` signature appeared inside repeated isolated-worker batches; its specific root cause is not proved. Codex must investigate these separately, not conflate them or label all RPC failures the root cause.

Do not merge PR #62, enable feature flags for normal use, change production files, registry/startup settings, close the user's interactive SW process, or overwrite a locked DLL without explicit separate user approval. Codex may implement scoped fixes on this PR branch and post results; keep changes reviewable.

## 2. Relevant classes (snapshot path and approximate symbol positions)

| Class/file | Purpose / important entrypoints | Scope |
|---|---|---|
| `Commands/CheckDrawingBom.cs` (~12,989 lines) | `CheckDrawingBom.RunBatch` (~249), `CheckOneDrawing` (~572), `AddFlatPatternSideChecks` (~1205), `CdResolveViewSideOutcome` (~1118), `CdBuildCandidateFlatSideProof` (~1944), `CdTryFlatSideDetailedOnSession` (~2941), `CompareWidthLengthField` (~6902), `TryGetVerifiedPartDimensionProperty` (~6634), `CheckAssemblyDrawing` (~7691). Coordinates selected BOM rows, opens Drawings, parses Notes/Tables/BOM and 2D/3D fields, Flat Pattern checker, cleanup, logging and Preview results. | **Edit carefully**, no wholesale rewrite |
| `Commands/CdFlatSidePilotPolicy.cs` (~325 lines) | COM-free decision policy `Evaluate` and `RunSelfTests`; `UseWorkingCopy / UseDirectRead / Unknown`; independently verified provenance flags. | Scoped changes allowed, keep fail-closed |
| `Commands/CdIsolatedVisibleFlatSideVerifier.cs` (~1,596 lines) | `VerifyViewOnce`, `ICdWorkerRuntime`, `AuditWorkerDocs`, `CloseAllWorkerDocuments`, `QuarantineAndTerminateOwnedWorker`, `LiveCdWorkerRuntime`. Copies/repoints Drawing & Part to temp, verifies exact worker PID/ROT, audits source ownership, scans geometry in worker, quarantines on COM/audit/cleanup failure. | Scoped crash/safety fixes allowed |
| `Commands/CheckUraOmote.cs` (~1,766 lines) | `UraOmoteChecker.CheckFlatDrawingView` (~747), `FlatDrawingViewSideResult`; legacy copy-based face-color classification. Legacy implementation considers **both painted sides** as front/ウラ (known ambiguity); current CHECK DRAWING pilot policy guards this without changing standalone command. | **READ ONLY**; request approval before broader edits |
| `Commands/CheckPartWorkingCopy.cs` (~394 lines) | `PrepareReferences` (~138) duplicates in-context graph. Throws when an already-open referenced model is Dirty, to avoid reading stale disk version. | **READ ONLY**; never remove Dirty barrier as a cosmetic fix |
| `Commands/ExcelDrawingCheckExporter.cs` (~1,917 lines) | `ExportToFile`; serializes the same field snapshots to Excel; report only on button click. | Preserve |
| `UI/DrawingBomCheckReviewForm.cs` (~1,205 lines) | `TriggerReportAction`; preview/report first, Excel on demand, navigate result. | Preserve |
| `ADDIN.csproj` | Explicit compilation of related files; x64. | Change only when required and explained |

### Existing production behavior
- `RunBatch` checks BOM-selected rows. Current 55-Drawing result (both Shadow and Pilot test): **11 OK / 0 NG / 44 WARNING**. Of 50 Flat Views, **13 legacy OK / 37 legacy WARNING**, 5 have no Flat field. Among 37 Flat WARNINGS, 34 are specifically nonlocked (`status=3`) in-context external references to Dirty Assemblies and 3 are Dirty Drawings (per latest detailed breakdown; initial 35/2 categorization was superseded). **Do not erase these WARNINGS without validated source freshness**.
- W/L: Drawing vs referenced Part Property numeric matches are OK even with manually entered Properties; genuine mismatch or unverified source WARNING; 0.01 mm tolerance and independent optional BOM W/L check. **Cut List independent Flat Pattern W/L validation is still a separate/uncompleted request; do not conflate it with crash work.**
- `合番`: valid link integrity may be OK while rendering unavailable in Silent; hardcoded `aa` is NG. `合番合計` can be N/A. Do not regress.
- `表/ウラ`: `表` = viewer sees colored/nonpink face, `ウラ` = pink face. Correct visible side -> OK, reversed -> NG, cannot prove -> WARNING. Missing or both-sides-pink appearance is ambiguous.
- Direct Read / isolated worker copies observe a saved-copy face, which is **not automatically proof** that the user's current in-memory Part/Assembly in-context geometry is synchronized.
- Fact 1 UI describes *observed side of copy*, Fact 2 explains *source synchronization WARNING*. 2D `BBOX MATCH` compares width/height only (0.35 mm tolerance), **not entire contour / holes**. `SA2-fr4` fake 10 mm arc bbox mismatch was corrected locally.
- **All five production/test switches are false by default:** `CdEnableDirectFlatSideVerdict`, `CdEnableReviewedFlatSidePilot`, `CdEnableIsolatedVisibleVerification`, `CdReviewedFlatSidePilotOptInForTestsOnly`, `CdIsolatedVisibleVerificationOptInForTestsOnly`.
- Current worker audit: `GetDocumentCount`/`GetDocuments` must agree; no external-path or .SLDASM documents; ownership verified; unknown/unreadable -> fail-closed; `remainingDocs != 0` including -1 -> failure; quarantine only exact worker-owned PID. If any worker error, field must be WARNING, never fall back to a false OK.
- Prior batch #38 `SA1-fr7` lost COM `0x800706BA` during close. Worker PID was quarantined and remaining 12 views continued on a new worker. Reported current batch baseline 49/50 worker verifies (one fail-closed), 50/50 2D BBOX in earlier clean run. Test files unchanged by hash in reported copies.

## 3. Crash signatures — verified evidence vs unresolved causal claims

**A — `0xC0000374`, heap corruption, `ntdll.dll` / `RtlFreeHeap` at SOLIDWORKS test-host exit**
- Minimal `startup_only` opens SOLIDWORKS with no Drawing and report says `ADDIN.dll` is **not loaded**; calling `NativeSession.ShutdownOwned -> swApp.ExitApp()` produces EV1000 ~0.88–0.99 s after BEFORE_EXITAPP, also in Visible mode. Reproduced in several variants: open/close only, Direct only, Working Copy only. `startup_no_exitapp` control showed 0 EV1000 twice.
- New report `0.5`: Anti captured local minidumps with native debugger (not on GitHub). Call stacks include `clr!CoEEShutDownCOM` / `slduiu!clrAssemblyLoader_c` / `swTaskpaneView_c::getAuDispatch` / `uiApiTaskPaneView_c` / `CSwAddinManager::loadAllAddins` and `ntdll!RtlFreeHeap`. This **localizes failure to SOLIDWORKS startup-add-in / COM/.NET teardown path**; does not identify which module corrupted heap.
- Startup add-ins observed (read-only registry inspection): XToolKCC, KIKUKAWA CAD V1, TAI TOOL (path to an ADDIN.dll that the report says currently absent). A Cybereason hook DLL and SWCEF were also loaded. **None is proven guilty**; do not disable/accuse/modify on user's live Windows profile.
- `UnloadAddIn` x3 returned rc=0 but still led to native exceptions, so runtime unload alone did not establish a clean no-add-in baseline.
- Code in CHECK DRAWING cannot reliably fix an unrelated host startup/teardown heap corruption. Codex should produce separate host diagnostic/mitigation and a minimal repro.

**B — `0xC0000005` in `sldapiu.dll+0x9a9bb0`, isolated-view worker**
- Previously recorded in long batch / after first opening/closing views; one batch worker returned RPC failure. Earlier A/B `QuitDoc` vs `CloseDoc` vs `CloseAllDocuments` did **not** eliminate Event 1000.
- New `0.5` tests, worker-only in its own verified PID: `worker_single` 1/1, `worker_multi8` 8/8, `worker_multi20` 20/20 all PASS, **0 EV1000** in those specific sessions. This suggests an **interaction with full RunBatch host/Dirty Assembly environment or timing** but does NOT prove that to be the root cause. No dump yet from the failing multi-component batch signature. Compare host+worker interactions and RCW/COM lifetime.
- Separate `slduiu.dll+0x1184d4a 0xC0000005` occurred after hidden host startup even with worker OFF: do not attribute it to worker code.

**C — RPC `0x800706BA` / `0x80010105`**
- Compatible with a server/worker that has faulted or become unavailable, often *secondary* to native faults. Prove timestamp ordering in each case rather than treating RPC code as primary cause. Retain fail-closed/quarantine behavior.

**D — Earlier signature `0xC0000005` in `mfc140u.dll` and more general add-in crashes**
- Prior crash investigations implicated `CheckDrawingBom.cs` and possible add-in interaction; this is historical context, not proof of a single current root cause.

**Key separation for conclusions:** `exit code 0` / `0 restarted workers` / synthetic self-test PASS does **not** mean no crash. Track Windows Event ID 1000/1001, WER and dumps by PID and timestamp.

## 4. Available evidence and constraints

- Definitive source report: `docs/reports/CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md`, newest section `0.5` includes native stacks, call ordering, add-in startup and 1/8/20-view trials.
- Build tested by Anti: Debug|x64; `run_selftest.ps1` PASS for `RunGobanLinkSelfTests` (Cases 1–33), `RunWidthLengthSelfTests`, `RunExcelAndPreviewSyncSelfTests` and `CdFlatSidePilotPolicy.RunSelfTests` (15 assertions), according to report. Build DLL hash in older run `BBE4C858...`; Codex must **compute the hash of its own actual tested build**, not reuse a stale hash.
- Full 13-phase suite: 8 phases exit 0 (but recorded EV1000 on exit); 5 were TIMEOUT with partial case outputs. Some targeted tests (both sides pink, flipped view, missing paint, Dirty source, locked ExtRef) previously passed individually; re-validate specific changed paths only after crash triage.
- Old 55 batch: 11 OK / 0 NG / 44 WARNING; worker test can be 49/50 due to quarantined worker; exported Excel, but TaskPane interaction on current user-locked/missing production DLL is **NOT TESTED**.
- Harness commands `Pr62Verify.exe`, native debugger `SwNativeDebug.cs`, logs and raw dumps were described as **local artifacts outside the GitHub repository**. Codex must not assume they exist in its checkout. Ask for specific harness/log files if inaccessible or create a clean MRE in repo, keeping synthetic tests separate from runtime tests.
- Avoid pushing raw minidumps or private absolute filesystem paths to public GitHub. Put sanitized stacks, module offsets, PID/timestamps, reproducible test commands and SHA-256 in report.

## 5. Work package for Codex (in priority order)

### P0 — independent root-cause audit, no code changes yet
1. Read full report, latest source and PR discussion. Build a call graph: `RunBatch -> CheckOneDrawing -> AddFlatPatternSideChecks -> (UraOmoteChecker.CheckFlatDrawingView / CdTryFlatSide... / CdIsolated... / CdFlatSidePilotPolicy.Evaluate)` and both `CloseAllWorkerDocuments` and main batch cleanup. Identify who owns every document, COM RCW, worker and host PID; find double-release, cross-thread COM access, dangling RCWs, `ExitApp` after release and unsafe tear-down suspects.
2. Separate four signatures `ExitApp heap`, `worker sldapiu access violation`, `hidden startup slduiu`, `RPC cleanup`. Rank hypotheses, give counterexamples and minimal reproductions. Review add-in startup/TaskPane/COM disconnect paths outside CHECK DRAWING **read-only** if relevant; do not presume CheckDrawing class alone caused host crash.
3. Reconcile whether no-ADDIN `startup_only` truly had no ADDIN.dll loaded and which other add-ins/hook DLLs existed at crash. If no clean Windows user/VM exists, mark missing baseline. Do not force changes in user's SOLIDWORKS startup configuration.

### P1 — minimum safe isolated crash repro & causal tests
4. Create **short single-case** reproducible host/worker test tooling with exact phase timestamps, PID, DLL hash, module list, WER detection, case-level result `PASS / FAIL_EV1000 / FAIL_EXCEPTION / TIMEOUT / NOT_RUN`. Test no Drawing + ExitApp, no ExitApp, AddIns enabled vs truly disabled in a **disposable** test profile, 1 worker view, several worker views with/without host activity. Controlled retries, not multi-hour marathon.
5. Capture call stacks/dumps only from disposable child PID. Ensure dump pipeline works; investigate teardown thread/call stack and native module owner. Avoid attaching/debugging/interfering with user's active SOLIDWORKS. Compare clean OS profile/add-in startup A/B without touching personal settings.
6. Code change only when causally justified; if host SW/add-in vendor issue unrelated to CHECK DRAWING, document a **separate resolution/mitigation** and provide proper steps for reproducibility; do not disguise root cause as CHECK DRAWING bug.

### P2 — targeted fixes and regression
7. If a worker/COM ownership issue is proven, make **small, scoped C# change**, keep `AuditWorkerDocs` / `CloseAllWorkerDocuments` / owned-PID quarantine checks, never close unknown docs or Save original file. Maintain user original session untouched; do not use blind sleeps or unbounded retries as the fix.
8. Keep 34 in-context nonlocked dirty-reference cases WARNING; observable face (Fact 1) can match while source synchronization (Fact 2) remains WARNING. Correct reversed face should be NG only if truly verified.
9. Validate 0 unwanted file changes (compare hashes), process/doc inventory, W/L numeric rules, 合番, 2D BBOX label, Preview/Excel field parity and Excel-on-click behavior. Default all five flags OFF. No cross-command changes unless explicit user approval.

### P3 — handoff
10. Commit scoped implementation to current PR branch (if implementation is justified), update a comprehensive final root cause report under `docs/reports/` and PR comment; **do not merge**. If not enough evidence for a safe fix, produce a verified investigation report and BLOCKED decision instead of ungrounded code. Provide user a transparent Test Matrix with baseline vs new EV1000 counts per mode, run commands, status of TaskPane UI on a rebuilt isolated DLL, and exact commit SHA.

## 6. Definition of done

- [ ] Root cause of each crash signature either experimentally attributed with reproducible evidence OR marked unresolved with explicit isolation/mitigation. Do not claim all crashes fixed based on one successful run.
- [ ] No new native errors/EV1000 in a sufficiently repeated, bounded test for the **specific changed path**, and stable shutdown (avoid conflating host-wide startup issue with plugin success).
- [ ] No skipped audit or fail-open; any worker COM failure -> WARNING + worker quarantine, never false OK.
- [ ] Run source-safe tests on copies; no original files modified and no user session killed/closed.
- [ ] No regression to W/L, 合番, 表/ウラ polarity, Excel-on-demand, UI.
- [ ] Five flags OFF in normal DLL; deploy/merge only with separate user approval.
- [ ] Report includes class/method changes, root-cause proof, remaining risks, test evidence, and PR/comment links.

**Codex should think critically and may challenge this proposed approach. It should explain an improved architecture if one demonstrably reduces complexity, crash risk and false verdicts—while respecting command scope and preserving the existing tested behavior.**
