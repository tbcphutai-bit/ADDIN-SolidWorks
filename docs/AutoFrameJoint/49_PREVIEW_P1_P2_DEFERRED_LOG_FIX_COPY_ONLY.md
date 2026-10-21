# Runbook 49 — Preview P1/P2 clicked but nothing displayed

Owner / C# author: ChatGPT. Implementer: Antigravity exact COPY ONLY.
Local real project: C:\Tao moi\ADDIN
Source branch: fix/afj-r49-preview-click-deferred-diagnostics-20261021

## What is observed

The uploaded SOLIDWORKS Output-Debug(1).txt reports a successful AFJ A/B selection and 90-degree estimated axes, but no AFJ-CUT-CANDIDATE or AFJ-PMP CUT execution. User explicitly says Preview was clicked. Thus the callback/return path is not visible in R48 diagnostics. R48 also invoked potentially expensive COM Boolean synchronously inside native PropertyManager OnButtonPress. This can be fragile and hides failed/stuck operations.

The Code Owner has NOT proved which specific failure occurred at runtime. Runbook 49 changes event scheduling and instrumentation. Manufacturing rules for Web/Wall/Lip remain unverified and both Frame cuts are still provisional P1/P2.

## Exact full ChatGPT-authored updated C# classes

(1) EXISTING: C:\Tao moi\ADDIN\UI\AutoFrameJoint\AutoFrameJointSelectionPage.cs
BEFORE Git blob SHA: 3557e67b92e5d78434b79e34d48f775b7bbfebb3
AFTER Git blob SHA: 0db9836a8cc87335cffcf288e8aeef217104418d
SOURCE: https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/fix/afj-r49-preview-click-deferred-diagnostics-20261021/UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs

(2) EXISTING: C:\Tao moi\ADDIN\Commands\AutoFrameJoint\AutoFrameJointGhostPreview.cs
BEFORE Git blob SHA: dff0c7ad4ab4b6d88ff6379994e2f3bbbba0aaa1
AFTER Git blob SHA: da151c81e49cfdaee3c2bdb455e2e4b531ff13f9
SOURCE: https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/fix/afj-r49-preview-click-deferred-diagnostics-20261021/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs

Compared against the original Runbook48 branch, only these TWO existing C# classes change. No new class, no ADDIN.csproj change, no other command/backend modifications.

## Code details

- UI prints [AFJ-R49] PMP UI loaded as soon as the PropertyManager is initialized.
- OnButtonPress logs button ID immediately, before any other operation.
- Buttons 107/108 schedule deferred preview on the SAME SOLIDWORKS owner UI STA thread using existing WinForms BeginInvoke; heavy Boolean does NOT execute during the native OnButtonPress callback. No background thread or timer.
- UI logs QUEUE, DISPATCH, PERFORM ENTER, selection errors, BACKEND CALL and RETURN, and PERFORM EXIT.
- UI preserves preview error/success message in the PropertyManager rather than allowing readiness updates to silently erase it.
- Backend logs every B-rep stage: ENGINE ENTER, CLEAN_PREVIOUS, ANALYZE_PAIR, SOLID_COPY_WORLD, INTERSECT_BEGIN/RESULT, BISECTOR_PARTITION_BEGIN/RESULT, CUT_TEMP_COPIES_BEGIN, DISPLAY_GHOST_BEGIN, ENGINE EXIT success=true/false with failure message.
- Same original temporary-body-only Booleans, AfterClose cleanup, and P1/P2 math as Runbook48: NO actual Part Feature Cut, NO Save. Not guaranteed to produce a visible candidate if solids have no true overlap or API fails.

## Antigravity copy-only requirements

1. Stay in the current real worktree C:\Tao moi\ADDIN. Do not switch or merge branches.
2. Read actual local two class files and calculate Git hashes using git hash-object -- PATH. BOTH MUST match exact BEFORE hashes listed here. Otherwise STOP, report SHA/diff; DO NOT overwrite concurrent edits.
3. COPY complete ChatGPT-authored C# content from the exact two GitHub links into their SAME existing local classes. DO NOT generate, rewrite, translate, or independently edit code. No sidecars/classes.
4. Verify exact AFTER blob SHAs above and inspect git diff --check, git diff --stat and git diff for the two target paths. Report any BOM/CRLF mismatch instead of force-resetting.
5. Confirm ADDIN.csproj, Debug x64 OutputPath, Pre/PostBuildEvent, AutoFrameJointReadOnlyProbe.cs, GeometryMath.cs, GhostSessionManager.cs, CommandEntry.cs, Check Bend, Check Hole, Omote, Mirror all untouched BY THIS TASK.
6. Report BEFORE/AFTER SHA, changed class paths and R49 SOURCE COPIED — BUILD NOT TESTED. STOP.

ABSOLUTELY NO ANTIGRAVITY BUILD, REBUILD, F5, DEPLOY, DLL/PDB write, COM registration, SOLIDWORKS restart, Git commit/push/merge/reset/stash/clean/checkout.

## Later USER-owned host test, only after Code Owner approval and manual Debug x64 Build

- Back up original DLL/PDB. Only user builds and starts SOLIDWORKS.
- On opening AFJ, expect [AFJ-R49] PMP UI loaded. If missing, DLL/code version uncertain.
- Click P1 once and collect Output [AFJ-R49] lines.
- Missing OnButtonPress id=107 -> button event never reached handler or wrong DLL.
- OnButtonPress present but no DISPATCH -> UI deferred callback/dispatch issue.
- DISPATCH with REJECT -> invalid selection/current document.
- ENGINE ENTER with INTERSECT_BEGIN then failure -> actual Boolean or empty solid overlap.
- DISPLAY_GHOST_BEGIN then error -> temporary Display3/transform problem.
- ENGINE EXIT success=True but nothing on screen -> graphics placement/display problem.
- Send precise Output and PMP status text; do not repeat a large number of tests.

STATUS: STATIC SOURCE ONLY. NOT COMPILED, NOT HOST-TESTED. NOT PRODUCTION CUTTING RULE.
