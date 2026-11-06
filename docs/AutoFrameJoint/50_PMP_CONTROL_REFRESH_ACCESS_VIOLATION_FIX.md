# R50 — PMP control refresh AccessViolation repair

Date: 2026-10-22. **Source repair applied; runtime result UNVERIFIED.**

## Evidence and diagnosis

The supplied debugger screenshot stops in `AutoFrameJointSelectionPage.UpdateReadyState`, at the native `analyzeButtonControl.Enabled` assignment (line 763 in the user's pre-fix source). The original private log shows this order:

1. A/B selection becomes READY with one component per selection mark.
2. `AUTO ANALYZE START` appears.
3. The injected read-only analyzer returns a complete report, explicitly stating `Cut geometry: NOT GENERATED`.
4. Another READY trace appears, followed by an unhandled `System.AccessViolationException`; SOLIDWORKS exits with code 1. No `AUTO ANALYZE END` is logged.

Source inspection connects that sequence to `PerformAnalyze` → `finally UpdateReadyState()` → native button `Enabled` setter. The old exception handler also attempted more native `Enabled` writes. The current installed source used the earlier P1/P2 UI; the PR #40 source is a later variant. The patch was adapted independently to each baseline.

**Proven:** the screenshot identifies the failing call, and analysis completed before the exception. **UNVERIFIED:** why the native control failed. The log contains no `OnClose`/`AfterClose` before this exception; an already-closed page is not an established cause. Native memory corruption remains possible. WPF binding warnings in the same log are not evidence of causation. The loaded AFJ call stack contradicts the unrelated screenshot claim that the loaded DLL contains no AFJ code.

The installed and checkout interop assemblies inspected read-only both report version `32.5.0.48` and matching SHA-256 for each assembly:

| Assembly | SHA-256 |
| --- | --- |
| SolidWorks.Interop.sldworks.dll | `fa85c4e9b26d0ba996c53362e95afe42ab563c96f27d87cd6809d262f3d3c4cb` |
| SolidWorks.Interop.swpublished.dll | `bf6e6ceff4db585a2f9e0de9034f93a7f6e2307632cb1f1155554f9854a78962` |

This does not prove overall interop compatibility, but provides no evidence for a mismatch between these inspected copies. No re-registration or COM activation was performed for this repair.

## Changed source

Only `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` changes in this PR.

| Methods | Repair |
| --- | --- |
| `BuildControls`, `UpdateReadyState`, new `ApplyReadyState` | Remove every native `.Enabled =` assignment, including initial disables. Buttons are enabled by their creation options. Validate selection and busy state when actions run. Ready refresh posts work instead of writing native controls from a backend `finally`. |
| `Show`, `AfterActivation`, `OnClose`, `AfterClose`, new `IsUiLive` | Check `Show2` status; gate native UI work by activation, page epoch and owner thread. Invalidate queued work when closing. Clear references and notify cleanup once. A closed instance cannot be shown again. |
| `PostUi`, `QueueBackendAction`, `QueueCutCandidate`, `OnButtonPress` | Queue Analyze, Show A/B and Preview on the existing UI dispatcher. Coalesce/reject repeated work while queued or busy. No worker-thread COM and no synchronous fallback when dispatch is unavailable. |
| `PerformAnalyze`, `PerformShowBodies`, `PerformCutCandidate`, `QueueAfterAction` | Recheck A/B identity after callbacks. Store results only for the current pair. Backend `finally` posts a refresh; exception paths log and update managed state without retrying native controls. |
| `SetCaption`, `TraceUiFault` | One guarded label-caption setter, cached unchanged captions, begin/end traces by control ID and page epoch. A caught control exception blocks further UI actions in that page instance. Fault logging includes HRESULT, exception and stack, without additional COM calls. |
| `QueueSelectionRefresh`, `QueueInitialFocus`, `QueueFinishPage` | Retain deferred focus and auto-analysis; discard obsolete work after close. Finish posts a close and refuses it while known backend work is pending. |

### Visible behavior

Action buttons now remain enabled. With an incomplete/invalid A/B selection, invoking an action displays its validation reason and does not run the backend. Repeated clicks while a job is pending or running are rejected. This deliberately avoids the native setter at the observed crash location.

The original A/B marks (1/2), component filters, selection identity checks, IDs, labels, layout and callback signatures are preserved for each baseline. No selection command, solver rule, geometry construction, Boolean, feature editing, save, rebuild or deployment logic is added.

Posting via WinForms `BeginInvoke` schedules work on the owning thread. It is not a guarantee that SOLIDWORKS cannot pump nested native messages. Lifecycle guards reduce invalid access paths; they do not prove native memory integrity or recover a corrupted process. No `HandleProcessCorruptedStateExceptions`, global corrupted-state policy or forced COM release was added.

## Two source baselines

| Variant | Before SHA-256 | After SHA-256 | Delivery |
| --- | --- | --- | --- |
| Current local P1/P2 UI | `49cb55d564bdab3ef5a2472ef85475727c793d361e300a26af5fd26bde18dae8` | `17a7cc2e448e2d71c5fc88af19fc99f1a710773e541a1b84de0e66ca47d919fe` | Applied as a targeted edit to the existing source; original byte backup retained privately. [Compatibility diff](PmpControlRefreshFix/LOCAL_P1_P2_COMPATIBILITY.patch). |
| PR #40 KEEP / KEEP-CUT UI | `48a02fae0ba02c0f6126ee68e0db10bf17892771c6e982ceb495c37a45b0377c` | `e76901e4c995773829a57a682f54f1f6bda9d8acc0c09bb0f8643b4b20b30591` | Normal source change in this branch. |

Branch: `fix/afj-pmp-control-refresh-20261022`, based on PR #40 commit `06ee880cb8a679e9efbbb276b860aad4f3dea634`. This is a dependent Draft PR targeting `feat/afj-profile-aware-cut-solver-20261021`. It does not merge or update that source branch. PR #39 remains the research report.

The user's working branch and HEAD stayed unchanged. The current checkout was already dirty. A guarded single-file write checked the original hash before applying the local-compatible variant; no reset, checkout, stash or replacement with the PR #40 file was used. Protected-file hashes are in the evidence below.

## Checks performed and limits

| Check | Result |
| --- | --- |
| Static comparison of selection methods and `BuildControls` | Selection methods unchanged; `BuildControls` only loses four initial `Enabled=false` assignments. |
| Native setters | Zero `.Enabled =` assignments; one guarded `.Caption =` assignment. |
| Working branch/HEAD; protected project/binary/backend hashes | Unchanged when the local patch was applied. See protection evidence. |
| `git diff --check`, changed-file review | Run before commit. Only one production UI class plus repair documentation/evidence is included. |
| Main Add-in Build/Rebuild/F5/Deploy | **NOT_RUN**, as requested. No project, binary or original CAD is changed by this repair. |
| Compilation, tests, SOLIDWORKS UI execution after patch | **NOT_RUN**. No new runtime PASS claim. The already loaded DLL remains unchanged. |
| Native root cause; crash elimination; solver production stability | **UNVERIFIED**. This repair removes the observed failing call and improves diagnostics. |

## Code Owner integration and manual verification

Review the source and this evidence first. For a checkout containing PR #40, apply the normal UI diff from this dependent PR. For the older P1/P2 source, review the compatibility patch against the recorded original SHA; if it differs, adapt the method changes instead of replacing the whole file. The current user's source already has that compatible edit. No `.csproj` entry is needed.

The compatibility artifact is a **zero-context unified diff**; Git requires `--unidiff-zero` when checking/applying it. Its safe use depends on the exact recorded baseline hash. It is not intended to apply on the PR #40 source or to be reapplied to the already patched local file.

The following are **manual verification steps for the owner; they have not been executed**:

1. In a recovered/healthy SOLIDWORKS session, use the owner's normal manual build and deployment procedure. Verify the loaded DLL corresponds to the new source; editing source cannot change an already loaded DLL.
2. Open an assembly without saving changes. Open AFJ; inspect `[AFJ-R50] ACTIVATED` and `SHOW`. Select Frame A then B. Confirm auto-focus and auto-analysis still work.
3. Confirm `BACKEND BEGIN/END Analyze`, matching `CAPTION BEGIN/END id=...`, and `AUTO ANALYZE END`. Confirm geometry labels update without another AccessViolation.
4. Click Analyze manually, Show A/B, and both Preview modes. Preview can return an existing `UNVERIFIED_RULE`; this UI repair does not enlarge the solver's verified family. Check backend logs separately from UI results.
5. Clear a selection and invoke an action: expect validation with no backend action. Try rapid repeated clicks: expect busy/queued rejection. Change the pair and verify old labels/results do not persist.
6. Close using Finish, OK and Cancel in separate runs, then reopen AFJ. Confirm cleanup once and no late native access; queued obsolete work should log `DROP stale/inactive UI work` if it is dispatched. Do not treat absence of a DROP trace as failure when the dispatcher was disposed before delivery.
7. If a native exception remains, retain the debugger call stack and the last R50 control ID/epoch begin/end traces. Stop using that corrupted SOLIDWORKS process. Do not infer a successful fix from source inspection alone.

## Evidence

- [Patch audit and preserved selection methods](PmpControlRefreshFix/PATCH_AUDIT.json)
- [Local application and protected-file hashes](PmpControlRefreshFix/LOCAL_APPLICATION_PROTECTION.json)
- [Sanitized original-log excerpts](PmpControlRefreshFix/ORIGINAL_FAILURE.sanitized.txt)
- [Diagnosis/evidence status](PmpControlRefreshFix/DIAGNOSIS.json)
- [Compatibility diff for the older local UI](PmpControlRefreshFix/LOCAL_P1_P2_COMPATIBILITY.patch)

The raw log, screenshots, original source backups, machine paths and CAD are kept out of this publication. Excerpts identify their original 1-based line numbers; sample names are aliased and CAD coordinates are omitted.

## API references

- SOLIDWORKS documents [AfterActivation](https://help.solidworks.com/2024/English/api/swpublishedapi/SOLIDWORKS.Interop.swpublished~SOLIDWORKS.Interop.swpublished.IPropertyManagerPage2Handler9~AfterActivation.html) as the activation notification.
- [Show2](https://help.solidworks.com/2021/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IPropertyManagerPage2~Show2.html) returns a `swPropertyManagerPageStatus_e` status.
- Microsoft describes [AccessViolationException](https://learn.microsoft.com/en-us/dotnet/api/system.accessviolationexception?view=netframework-4.8) and the limits of handling corrupted-state failures. The repair does not rely on catching an AccessViolation to continue.
