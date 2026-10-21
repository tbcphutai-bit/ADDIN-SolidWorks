# Runbook 40 — Smooth component selection A → B (ChatGPT-authored real C# source)

**CODE OWNER:** ChatGPT. **IMPLEMENTER:** Antigravity strictly copies exact code into the active Visual Studio source. **NO AUTO BUILD.**

## 1. Evidence from user's attached debug log

The supplied 262-line SOLIDWORKS Output log shows:
- `C:\Tao moi\ADDIN.dll` loads successfully, `[TAI STARTUP] ConnectToSW total ms=404`.
- `[AFJ-READONLY]` identifies Frame A length 1500 mm, Frame B length 3200 mm, angle 90°, `B = LONG / A = SHORT`.
- `[AFJ-GHOST] AfterClose CLEAN` appears twice.
- `[MAKE HOLE SAFE] Cleanup skipped...` repeated and numerous `System.Windows.Data Error` messages are present; these do NOT prove an Auto Frame Joint selection failure.
- End of log reports `SLDWORKS.exe exited with code 4294967295 (0xffffffff)`. Log alone cannot attribute that exit to Auto Frame Joint; debugger termination may also generate this code.
- **No `[AFJ-SEL]` tracing exists in the currently loaded source**, so this log cannot measure or diagnose per-click selection delays.

## 2. Verified source issue and restricted goal

In the baseline `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`, the first box A is focused after `page.Show2(0)`, but there is NO transition to focus box B when A receives a selection. This forces a separate click in B.

SOLIDWORKS API Help explicitly says `OnSelectionboxListChanged` executes DURING a selection, and code must not change selection lists/models in that callback. SOLIDWORKS also provides `IPropertyManagerPageSelectionbox.SetSelectionFocus()` and `GetSelectionFocus()`. .NET WinForms `Control.BeginInvoke` queues an asynchronous action on the window-handle-owning UI thread.

**New behavior (one small experimental source patch):**
- Pick A once → asynchronously focus B on the existing UI message loop **after** the native selection callback has returned.
- Pick B → Analyze and View A/B become enabled as before.
- Do NOT change A or B selected components, selection marks (1/2), filters (swSelCOMPONENTS), analysis or GhostPreview behavior.
- If auto-advance fails, user can still click B manually.
- Never auto-run expensive `AnalyzePair` or `Ghost.TryShow` on selection events. No new threads, Task.Run, background timers, or COM model changes.
- Preserve explicit COM error log evidence with new `[AFJ-SEL]` prefix.

## 3. Code to copy — EXACT single file

Branch: `fix/afj-pmp-deferred-focus-b-source-only-20261021`  
Base commit: `72f107bc4e40216462c0e6a66132976f46257485`

**ONLY C# file changed:** `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`.

ChatGPT wrote the complete amended C# class in the GitHub commit. Antigravity must copy the **entire exact committed file**, not synthesize variants. Core new method is implemented in class:
```csharp
private void QueueFocusB()
{
    if (!shown || closing || focusBQueued || deferredFocusWindow == null)
        return;
    focusBQueued = true;
    try
    {
        deferredFocusWindow.BeginInvoke(
            (System.Windows.Forms.MethodInvoker)delegate
            {
                focusBQueued = false;
                if (!shown || closing || selectionB == null ||
                    !IsSameDocumentActive())
                    return;
                try
                {
                    if (selectionA != null &&
                        selectionA.GetSelectionFocus() &&
                        Selected(MarkFrameA) != null &&
                        Selected(MarkFrameB) == null)
                    {
                        selectionB.SetSelectionFocus();
                        Trace.WriteLine("[AFJ-SEL] Advanced focus A -> B");
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("[AFJ-SEL] Focus advance failed: " + ex);
                }
            });
    }
    catch (Exception ex)
    {
        focusBQueued = false;
        Trace.WriteLine("[AFJ-SEL] Cannot schedule focus: " + ex);
    }
}
```

The committed class also contains `InitializeDeferredFocusWindow()` to create a WinForms UI handle on the SOLIDWORKS UI thread; `DisposeDeferredFocusWindow()` to dispose after Cancel/AfterClose AND page.Show2 failure; and updated `OnSelectionboxListChanged` logging `[AFJ-SEL] List box=... count=...` then posting `QueueFocusB()` for A count=1.

**Safety rule:** `OnSelectionboxListChanged` itself must NOT call `SetSelectionFocus`. Only the deferred UI-thread delegate may call it; even there, guard original Assembly, page life, selection A selected, B empty and A still focused.

## 4. Antigravity's direct paste instructions

**Target is the actual open project** `C:\Tao moi\ADDIN\ADDIN.csproj`. No detached-worktree-only completion.

1. Check `git status -sb`, `git rev-parse HEAD`. Do not overwrite uncommitted files.
2. Check current local `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` via `git hash-object`. Expected BASE SHA: `743a93a9f737b00b8e5e6e6e1148fc0de758ba2d`. If it differs, STOP and send local diff for ChatGPT reconciliation.
3. Copy the complete C# source from this branch/commit into this ONE file.
4. Confirm expected NEW Git blob SHA from ChatGPT's commit message; verify local `git hash-object`.
5. Confirm `ADDIN.csproj` already compiles the selection page and retains **Debug x64 OutputPath `C:\Tao moi\`** with empty Pre/PostBuild events. **Do not edit .csproj** in this change.
6. Send Code Owner: branch/HEAD, Git diff -- this ONE C# file only, blob hash, any warnings, and exact class lines if discrepancies found. No self-written fixes.
7. No Build, Rebuild, Debug F5, SOLIDWORKS start/stop, DLL/PDB deploy, commit or merge without user permission.

## 5. After user manually approves Rebuild

Because Debug x64 `OutputPath=C:\Tao moi\` directly replaces the installed DLL/PDB, stop ongoing debug and back up both files before any manual Rebuild.

Host test on a copied Assembly:
1. Open Auto Frame Joint: cursor focus initially A.
2. Click any valid Part component A once. Check that focus moves to B automatically, A selection remains.
3. Click B: two components selected, buttons ready; `[AFJ-SEL]` logs show focus A→B.
4. Clear B then select B again; no unexpected auto-clear A.
5. Change A while B already selected; no unwanted focus jump.
6. Select the same Component twice (reject), try wrong type, and repeat Cancel/re-open 10 times.
7. Cancel after ghost preview; previous Temporary Bodies must disappear, no crash or COM exception; Feature Tree and Part source unchanged.
8. If SOLIDWORKS exhibits a selection reentrancy issue, stop using this patch and restore the original SelectionPage while Code Owner re-evaluates event ordering.

**Honest current status:** Experimental source-level UX improvement. NOT BUILT or SOLIDWORKS HOST-TESTED. The log doesn't prove why the user experiences a lag beyond the missing auto-focus.
