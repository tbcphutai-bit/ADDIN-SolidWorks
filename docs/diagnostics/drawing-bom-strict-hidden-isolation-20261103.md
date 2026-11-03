# CHECK DRAWING BOM – strict hidden + native-close isolation (2026-11-03)

## Purpose / not a production fix

Two local Visual Studio runs reported native heap corruption `0xC0000374` inside `CloseDoc` (sixth Drawing) and inside `QuitDoc` (seventh Drawing) while processing 12 files. Native dump / stack is still required to locate the memory corruption source. The previous implementation was *not visually silent* because it called `DocumentVisible(true)`, `IModelDoc2.Visible=true` and `ActivateDoc3`.

This **A/B code test** changes `Commands/CheckDrawingBom.cs` only. It isolates rendering/activation/close operations so the next live test is informative. It is NOT a confirmed crash solution and cannot yet provide a trustworthy complete BOM comparison.

## What is implemented in code

- Only when `RunBatch(..., silent:true)`:
  - Limit the run to **12 Drawing files**, once per SOLIDWORKS process (when a hidden drawing was successfully opened). Stop later attempts until SOLIDWORKS has restarted.
  - Call `ISldWorks.DocumentVisible(false, swDocDRAWING)` **before** `OpenDoc6(Silent|ReadOnly)`, then restore the previous global load-visibility flag in `finally`.
  - Skip the in-process `DrawingCommandDisplayScope` and its view snapshot/restore actions.
  - Skip `ActivateDoc3` / `model.Visible=true` / `GraphicsRedraw2` for hidden Drawings.
  - Temporarily skip `AddFlatPatternSideChecks` (the `CheckUraOmote` working-copy path). Emit `NOT CHECKED`/Warning, never OK.
  - Retain newly opened hidden Drawing documents in SOLIDWORKS session memory rather than triggering the known native `CloseDoc` / `QuitDoc` crash path. Log `[STRICT HIDDEN HOLD]`. **Do not use this branch for large batches or multiple runs.** Restart SOLIDWORKS after a test (save user work first).
  - Even if checks execute, mark each row as `Warning` with `STRICT HIDDEN LINK VALIDATION`; unrefreshed `$PRPSHEET/$PRPVIEW` values may be stale or blank. Do not treat NG/OK from cached displayed text as verified.
  - Log `[STRICT HIDDEN OPEN]`, `[STRICT HIDDEN READ]`, `[STRICT HIDDEN HOLD]`, `[STRICT HIDDEN END]`.
- Non-silent batch cleanup remains the same as the parent diagnostic branch.
- No edits to `CheckPartWorkingCopy`, `CheckUraOmote`, `DrawingCommandDisplayScope`, UI, `ADDIN.csproj` or output paths.

## Hand-off to Antigravity

1. Inspect project Git branch, `git status`, local modifications. Back up `Commands/CheckDrawingBom.cs` and do not discard any changes.
2. Git branch: `fix/drawing-bom-strict-hidden-no-close-isolation-20261103`. Compare against `fix/drawing-bom-owned-drawing-quitdoc-probe-20261103`. **Only apply the diff in `Commands/CheckDrawingBom.cs`** (or cherry-pick carefully if there are no local conflicts).
3. Retain the user's existing Debug x64 configuration, output DLL/PDB copy post-build commands, references and existing UI. DO NOT adjust other command classes.
4. Static review only. Do NOT auto build, run, restart SOLIDWORKS, push or merge. User/Codex does the build and live debugging.
5. On user-approved tests, use file copies and confirm loaded DLL timestamp/path. Start from a freshly restarted SOLIDWORKS session with saved models. Run 1 Drawing first, then up to 12 on another fresh session. Capture the new trace markers, active document and window flicker, and number of open docs.
6. `Warning` is expected; read values/flat checks are **not verified** in this diagnostic. If a Drawing unexpectedly shows or the process crashes in another API, stop, record first offending step and capture a native dump/stack.
7. This experimental branch MUST NOT be merged into production until real hidden-mode property evaluation, complete flat checks and deterministic safe document cleanup have been designed and tested. It intentionally retains native documents. After a run, save original user documents first, then exit/restart SOLIDWORKS to free hidden resources.

## Suggested next steps after evidence

- If 12 hidden drawings can be traversed without crash: introduce an isolated, explicitly owned document-lifetime controller that releases all drawing/view/note/part RCWs only after processing finishes and closes documents safely; test under native crash dump.
- If hidden-linked values are blank/cached, implement a read-only resolver for `$PRP/$PRPSHEET/$PRPVIEW` with explicit source model/config resolution and comparisons against known visible ground truth. Until then return Warning (not a false OK).
- If hidden Drawings cannot be processed, report the incompatible check rather than silently activating the UI.

## Status

Static checks performed: code diff confirms a single modified C# file; balanced braces; no Win32 subclass introduced. No local SOLIDWORKS interop compiler or native process is available in this environment: **no claim of successful build or run.**
