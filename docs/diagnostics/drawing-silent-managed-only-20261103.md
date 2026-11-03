# Drawing silent mode — managed-only A/B diagnostic (2026-11-03)

## Why this branch exists

Observed while running CHECK DRAWING BOM (12 selected rows): the SOLIDWORKS process reported native heap corruption (0xC0000374) during the batch. This is NOT proof that DrawingCommandDisplayScope caused the corruption. The existing implementation installed a native subclass on the shared MDI client and blocked WM_SETREDRAW, while SOLIDWORKS was opening/activating/closing drawings and temporary parts.

This branch **isolates that risk**. It is a diagnostic patch, NOT a verified production silent-mode implementation.

## Changed files

1. `Commands/DrawingCommandDisplayScope.cs`
   - Remove Win32 P/Invoke, SetWindowSubclass, message interception and redraw suppression.
   - Keep prior CommandInProgress, drawing visibility, original document/sheet and existing drawing display-state capture/restore.
   - Preserve class name, constructor, IDisposable contract and existing caller APIs.
   - New trace markers: `[DRAWING SCOPE MANAGED BEGIN]` and `[DRAWING SCOPE MANAGED END]`.

2. `Commands/CheckDrawingBom.cs`
   - Add checkpoints around `CloseDoc` and restoring the originally active document for each row.
   - No changes to Drawing BOM comparison logic or DXF export logic.

`ADDIN.csproj` is unchanged: it already compiles `Commands/DrawingCommandDisplayScope.cs`.

## Intentional tradeoff

`OpenDoc6` still uses SOLIDWORKS Silent/ReadOnly options where configured, but **Drawing windows may briefly repaint or appear** because this branch no longer freezes the MDI viewport. Do not claim that this version hides all UI. If the crash stops, a separate design phase is required to deliver a safe, genuinely non-intrusive UX.

## Local handoff for Antigravity / Visual Studio

1. Create a backup of the existing local `DrawingCommandDisplayScope.cs` and `CheckDrawingBom.cs`. Check `git status` and record any uncommitted edits. Never discard those changes.
2. Fetch branch `fix/drawing-silent-managed-only-diagnostic-20261103`. Inspect `git diff origin/main...origin/fix/drawing-silent-managed-only-diagnostic-20261103 -- Commands/DrawingCommandDisplayScope.cs Commands/CheckDrawingBom.cs`.
3. Apply the new scope implementation and only the cleanup-logging hunk in `CheckDrawingBom.cs` to the user's Visual Studio checkout. If existing local changes conflict, stop and report the exact conflicting lines rather than overwrite.
4. Do not change `ADDIN.csproj`, other command classes, add-in registration, GitHub main, output paths or external references.
5. Do not trigger automatic build or run. Ask the user to request/authorize their normal Debug x64 build workflow. After building, verify the actually loaded DLL belongs to the newly built output (avoid an old DLL).

## Manual verification — no success claim without running

- Confirm build in original configured environment (Debug x64).
- Use **copies of the same 12 Drawing/Part inputs** as the crash run. Save the original user model before testing.
- Execute CHECK DRAWING BOM in silent mode for at least three runs, allowing each run to finish or recording exactly where it crashes.
- Capture first `[DRAWING SCOPE MANAGED BEGIN]`, per-row `[BATCH CLEANUP]`, and final `[DRAWING SCOPE MANAGED END]` log entries.
- If `CloseDoc CALL` occurs without `CloseDoc RETURN`, collect native dump and stack at `CloseDoc`; likewise for `RestoreActive CALL` / `RETURN`.
- Check open document count, original active document/sheet, Drawing visibility, CommandInProgress restoration and no model/source data changes.
- Run CHECK DXF BOM once to ensure the shared scope does not regress this caller.
- If the crash persists without Win32 subclassing, do not assume the scope is responsible; next inspect `CheckPartWorkingCopy.Dispose()`, COM/document lifetimes, and native crash dump.
- If the crash disappears, report association only; repeat and use an A/B baseline before attributing root cause.

## Merge and rollback policy

- This branch should remain unmerged until local validation succeeds.
- To revert the A/B test, restore the backed-up local files or switch the test checkout back to its baseline. Do not revert unrelated uncommitted work.
