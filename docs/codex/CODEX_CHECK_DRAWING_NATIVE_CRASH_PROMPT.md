# COPY TO CODEX — diagnose native SOLIDWORKS crash & finish CHECK DRAWING safely

You are taking over **CHECK DRAWING native crash investigation** from Antigravity. You have full discretion to **reason independently, challenge prior proposed fixes, design the best final architecture and implement a scoped fix if evidence supports it**. Do not merely repeat Anti's observations or make cosmetic changes.

## Exact repository to work on

- Repository: https://github.com/tbcphutai-bit/ADDIN-SolidWorks
- Draft PR: https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/62
- Working branch: `fix/check-drawing-goban-display-retry-uraomote-20261108`
- Read **first and completely**: `docs/codex/CHECK_DRAWING_PR62_CRASH_HANDOFF.md`.
- Read source evidence: `docs/reports/CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md`, especially `0.3`, `0.4` and `0.5` (native minidump/call-stack, worker 1/8/20-view comparisons).
- Latest pre-handoff source state: `838c99753a4af101520b4ec2b16863ba9323e409` (docs-only; main functional code last updated at `2eae32668bdcb18421abc36ed7f150d0b7756703`).
- Target runtime: SOLIDWORKS 2024 SP5.0, C#, COM interop, Windows, Debug x64.

## First: map & audit all relevant classes

Read each class with its call sites. Produce an accurate actual call/ownership diagram, including:
1. `Commands/CheckDrawingBom.cs` — `RunBatch`, `CheckOneDrawing`, `AddFlatPatternSideChecks`, direct probe, Part/drawing paths and configs, W/L & 合番, `CheckAssemblyDrawing`, result/Excel flow and batch cleanup.
2. `Commands/CdFlatSidePilotPolicy.cs` — strict verdict and provenance decision, synthetic checks.
3. `Commands/CdIsolatedVisibleFlatSideVerifier.cs` — owned worker PID, ROT attach, COM RCWs, `VerifyViewOnce`, `AuditWorkerDocs`, `CloseAllWorkerDocuments`, `QuarantineAndTerminateOwnedWorker`, disposal.
4. `Commands/CheckUraOmote.cs` — read-only examination of old flat-face/Working Copy algorithm.
5. `Commands/CheckPartWorkingCopy.cs` — read-only examination of Dirty in-context graph safeguard.
6. `Commands/ExcelDrawingCheckExporter.cs` and `UI/DrawingBomCheckReviewForm.cs` — preserve Preview/Excel consistency.
7. The SOLIDWORKS add-in startup/TaskPane Connect/Disconnect path — inspect read-only even if outside CHECK DRAWING, because native minidump points toward TaskPane/add-in CLR shutdown.

You are NOT authorized to rewrite unrelated commands. If a crash demonstrably requires a broader startup/disconnect change, document the proposed minimal diff and ask for separate approval before changing it.

## Known evidence — do not misstate

- The old 55-Drawing batch remains `11 OK / 0 NG / 44 WARNING`. `表/ウラ`: 13 OK, 37 WARNING, 5 N/A. 34 cases involve Dirty nonlocked in-context Assembly refs; 3 Dirty Drawings. **Those 34 must remain WARNING until live-source freshness is proven**.
- All five new production/pilot/isolated flags OFF by default. Preserve that.
- The `0xC0000374` exit crash reproduces even in `startup_only` (no Drawing, no ADDIN.dll loaded) after harness `swApp.ExitApp`. Native dump stacks show `clr!CoEEShutDownCOM`, `slduiu` TaskPane/add-in manager and `ntdll!RtlFreeHeap`. This does NOT prove an individual third-party add-in is guilty; isolate under clean disposable profile before attribution.
- Host startup add-ins observed: XToolKCC, KIKUKAWA CAD V1, TAI TOOL; other module hooks may also be loaded. Do not modify the user's real Startup/registry or disable production add-ins.
- Worker `0xC0000005 sldapiu.dll` occurred in large joint batch; isolated worker tests for 1/8/20 Views had 0 EV1000. Host `slduiu.dll 0xC0000005` also occurred with worker OFF. Separate signatures; no invented unifying root cause.
- RPC `0x800706BA` and `0x80010105` may occur AFTER native failures. Current worker fail-closed audit/quarantine design is intentional. Do NOT remove it to make tests pass.
- Latest functional safety code is build-tested and synthetic-tested per report but not proved safe in real user TaskPane deployment. Source files and native dumps may exist only on Anti's local machine, not this repo.

## Deliverable plan and execution

**Stage A — analysis first; do not edit until you have evidence.**
- Write a concise root-cause hypothesis matrix (signature, process/PID, loaded modules, actual native stack, minimal trigger, counterexample, confidence, proposed decisive test).
- Audit COM lifetime (STAs/threads, RCW ownership, FinalReleaseComObject, pointers retained beyond CloseDoc, reentrant callbacks, cancellation, worker attach/detach) and host exit behavior (ExitApp vs proper owned shutdown).
- Identify if startup/TaskPane third-party issue is independent of CHECK DRAWING; do not blame CheckDrawingBom just because it is large.
- Identify unsafe cleanup possibilities only from actual source and evidence, including any use of CloseDoc/QuitDoc/ExitApp on documents not proved command-owned.

**Stage B — bounded isolated reproducer.**
- Implement or adapt a test harness; one case per owned SOLIDWORKS process, in a disposable environment, explicit timeout and deterministic teardown, protect main user PID.
- Repeat small A/B: startup with/without add-ins in a clean test profile; visible vs hidden; ExitApp vs alternative validated owned shutdown; worker-only 1/8/20 views; worker combined with host+dirty Assembly. Avoid long random multi-hour batch runs.
- Capture and parse EV1000/1001, raw WER and NATIVE CALL STACKS in sensitive local-only dumps, include exact times/module offsets/SW build/add-in module inventory. Never publish raw .dmp, personal paths, proprietary CAD files or tokens.
- If crash signature cannot be reproduced under the available setup, mark BLOCKED and provide next minimal input needed; do not invent a fix or claim success.

**Stage C — implement one consolidated justified change, if possible.**
- Make the smallest causal fix within CHECK DRAWING/isolated-worker lifecycle: bounded retries, owned-PID-only control, explicit stage/fault results, no COM failure misread as OK, no file writes. Preserve existing tests and user interface.
- If actual root cause lies in host startup add-ins, produce a separate host-mitigation recommendation, not a fake CHECK DRAWING patch. If any outside-scope code must change, explain and request user approval.
- Preserve exact business rules: W/L equal values OK even if manual; mismatched W/L WARNING; valid 合番 link and hardcoded bad values handled; correct 表/ウラ OK, reversed NG, unknown WARNING; 34 dirty ExtRefs WARNING; Fact 1 = observed saved Drawing face only, Fact 2 = in-memory source sync warning; BBOX W/H only; Excel on click and Preview/Excel parity.
- All five flags OFF in normal builds. Never merge PR or overwrite user production DLL.

**Stage D — verification and GitHub handoff.**
- Debug|x64 compile and deterministic tests (include fault injection). Give exact build command, output DLL SHA-256 and changes from baseline.
- Test native cases with repeat counts and EV1000 results. For any test that exits code 0 but records a Windows crash event, mark `FAIL_WITH_EV1000`. Separate `TIMEOUT`, `NOT RUN`, `BLOCKED`.
- Verify before/after open-doc path inventory, owned vs foreign PID, zero modified source files, no assembly/part saves, no live user session disruption.
- After native stability is understood, test small Copy-only TaskPane/Excel UX with approval. Run 55 Drawing batch only when safe/relevant; do not make it the first test.
- Create a new report in `docs/reports/CODEX_CHECK_DRAWING_CRASH_FINAL_REPORT.md` containing:
  1. exact HEAD, baseline commit, changed files/functions, build SHA
  2. full class diagram and root cause by signature with confidence & experimental evidence
  3. native stack findings, other loaded add-ins and A/B results
  4. source-integrity checks and worker safety guarantees
  5. regression table for W/L, 合番, 表/ウラ and Preview/Excel
  6. unresolved problems and whether production is safe
  7. single recommended final design and any user action required
- Commit scoped changes to this **existing** PR #62 branch, post PR comment with report and SHA; **DO NOT MERGE**.

## Decision criteria

Prefer a trustworthy documented **BLOCKED / host defect** over a false “fixed” claim. Your job is to make the overall command stable, not merely reduce reported warnings or silence Windows Event Log.

**First response to user:** summarize your independent diagnosis, explain whether a host-wide crash is separable from CHECK DRAWING, propose the best one-pass design, then implement what can be safely verified. Do not ask the user to repeat what is already documented here.
