# Runbook 41 — Fast native PMP Frame picking AND automatic focus A → B (ChatGPT code)

**Owner of all C# source:** ChatGPT. **Antigravity:** exact copy/paste implementer. **NO automatic Build/Rebuild/Deploy.**

## Goal confirmed by user

Fix BOTH user-visible UX complaints in ONE source patch:
1. Clicking a **Frame face/edge in the SOLIDWORKS graphics area** feels slow or sometimes not accepted as Frame A/B.
2. After selecting Frame A, the user should **NOT need to click selection box B manually**.

The final goal is:
```
Open Giao góc Frame -> focus A -> click Face/Edge/Component of Frame A once
  -> selection resolves owning Component -> auto focus B after callback
  -> click Face/Edge/Component of Frame B once -> analyze/preview enabled
  -> click Phân tích / Xem trạng thái A/B -> Cancel closes and Ghost is cleaned
```

## 1. Exact source to copy

- Repository: `tbcphutai-bit/ADDIN-SolidWorks`
- Branch: `fix/afj-selection-click-face-edge-fast-20261021`
- Base commit: `cefffb35851ea2890cdd281b6345f047e576fe54` (already includes the earlier deferred A→B patch).
- **CHANGED C# FILE ONLY:** `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`.
- Implementation preserves all existing selection marks (A=1/B=2), no new command, no Ghost or analysis code changes, no changes to ADDIN.csproj.

### Actual source behavior (already implemented)

1. `BuildControls()` configures each SelectionBox via `SetSelectionFilters` BEFORE `page.Show2`:
```csharp
int[] filter =
{
    (int)swSelectType_e.swSelFACES,
    (int)swSelectType_e.swSelEDGES,
    (int)swSelectType_e.swSelCOMPONENTS
};
selectionA.SetSelectionFilters(filter);
selectionB.SetSelectionFilters(filter);
```
SOLIDWORKS' **documented selection preference** is that `FACES+COMPONENTS` and `EDGES+COMPONENTS` promote a clicked Face/Edge to the owning Component; do **NOT** add `swSelCOMPSDONTOVERRIDE` because that would defeat the Component preference. If a specific host version still delivers a `Face2` / `Edge` object, `OnSubmitSelection` attempts `IEntity.GetComponent()` in assembly context as a defensive fallback. `GetSelectedObjectsComponent4` keeps selection extraction scoped to marks 1 and 2.
2. `OnSelectionboxListChanged` does **NO** `UpdateReadyState`, `SetSelectionFocus`, `ClearSelection2`, `Select4`, `GetModelDoc2`, `Display3`, `Hide`, or model mutation. It only logs `[AFJ-SEL] List...` and calls lightweight `QueueSelectionRefresh(id == BoxFrameA && count == 1)`.
3. `QueueSelectionRefresh` coalesces bursts into ONE WinForms `Control.BeginInvoke` callback on the current SOLIDWORKS UI thread. **AFTER the selection callback returns** it refreshes both buttons/state, then attempts `selectionB.SetSelectionFocus()` only if Assembly unchanged, A box still focused and selected, and B empty. If the dispatcher is unavailable it falls back to old ready-state path so manual picking still works.
4. `OnSubmitSelection` rejects unsupported types, checks original Assembly, obtains Component by direct `Component2` or `IEntity.GetComponent()`, prevents duplicate A/B occurrences, sets the item label to `candidate.Name2` and does **NOT** resolve/open Part documents. Uses Stopwatch, logging only selections whose synchronous submit callback took >=50 ms as `[AFJ-SEL] SLOW OnSubmitSelection ...ms`.
5. `DisposeDeferredFocusWindow()` is called on AfterClose and failed Show2. No Timer, Task.Run, ThreadPool, implicit COM thread or automatic cut.

## 2. Why this is a source experiment, not a verified performance result

**SOLIDWORKS official documentation:**
- `SetSelectionFilters`: a selection box with `swSelFACES` + `swSelCOMPONENTS` places a **Component** in the box on a graphical-face click; edges work similarly.
  https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IPropertyManagerPageSelectionbox~SetSelectionFilters.html
- `IPropertyManagerPage2Handler9.OnSelectionboxListChanged` is called DURING selection and must not affect the model or selection list.
  https://help.solidworks.com/2025/english/api/swpublishedapi/SOLIDWORKS.Interop.swpublished~SOLIDWORKS.Interop.swpublished.IPropertyManagerPage2Handler9~OnSelectionboxListChanged.html
- `IEntity.GetComponent()` gets the owning Component only for an assembly-context entity.
  https://help.solidworks.com/2016/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IEntity~GetComponent.html

Actual face/edge promotion, native focus handling and performance require host tests on the user's SOLIDWORKS version. A single C# patch cannot overcome inherently expensive native geometry picking on very large assemblies.

## 3. EXACT Antigravity copy/paste instructions

**Do not apply the old Runbook 40 patch first if it has not been applied.** The new source is a FULL combined version including its auto A→B fix.

**Actual target:** `C:\Tao moi\ADDIN\UI\AutoFrameJoint\AutoFrameJointSelectionPage.cs`.

1. Before editing, run in `C:\Tao moi\ADDIN`:
```powershell
git status -sb
git rev-parse HEAD
git hash-object "UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs"
```
2. Safe known source baselines:
   - Before Runbook 40 auto-focus: `743a93a9f737b00b8e5e6e6e1148fc0de758ba2d`.
   - After Runbook 40 auto-focus: `6311feff470117a7f251d710b200c7c202a8ceaf`.
   - If the local SHA is NEITHER, STOP and send exact local diff to ChatGPT, because another agent may have changed the class.
3. When baseline is verified, copy the **entire committed** `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` from this Runbook 41 branch into the local target source file (exact content; do not independently write/change any API).
4. After copy, run `git hash-object` again and compare to the exact NEW blob in the GitHub commit (printed by ChatGPT in the handoff). DO NOT accept a mismatch.
5. Inspect `ADDIN.csproj` **read-only**: make sure it still includes SelectionPage exactly once, Debug x64 OutputPath remains `C:\Tao moi\`, and PreBuild/PostBuild events are empty. Do not modify the csproj in this patch.
6. Report to ChatGPT the branch/HEAD, before/after SHA, `git diff --stat`, actual changed files, and any copy conflicts. Do NOT create new classes, edit backend, generate another selection algorithm or merge without user instruction.

**HOLD:** No automatic Build, Rebuild, DLL/PDB copy/Deploy, SOLIDWORKS start/stop, Visual Studio F5 or registry changes. Source-only until user manually approves. Because Build OutputPath is the installed `C:\Tao moi\` directory, when building later ensure other debug is finished and a complete backup of DLL+PDB exists.

## 4. Manual acceptance tests after later authorized Rebuild

Use a COPY of the same user Assembly that showed selection friction; keep real CAD untouched.

| Test | Expected |
|---|---|
| Click broad planar Face A, B | Each accepted as correct named Component |
| Click thin Wall/Lip face | Component selected or explicit logged rejection reason; no crash |
| Click a visible Edge A, B | Owning Component selected |
| Click an occurrence of repeated Part file | Correct COMPONENT OCCURRENCE; do not confuse same Part document with same instance |
| First A click | Focus automatically advances to box B without additional click |
| Clear/change A while B empty | No wrong item persists; guide user to select B |
| Select same Component in both boxes | Duplicate rejected |
| Change document during PMP | No ghost/model/selection mutation in different Assembly |
| Analyze and Xem trạng thái A/B | Old read-only result/ghost behavior preserved |
| Click Cancel after Ghost | All temporary body overlay removed; source Part unchanged |
| Repeat 10 times | No stuck focus, lingering ghost or slowdown |

Collect Debug Output lines containing `[AFJ-SEL]`, particularly `SLOW OnSubmitSelection`. If native callbacks still show high latency (>50 ms) or face/edge selection misbehaves, provide a video with visible mouse clicks plus log; ChatGPT will write next precise correction based on facts.

**This source patch has NOT been compiled/host-tested. Do not claim UX smoothness is proven yet.**
