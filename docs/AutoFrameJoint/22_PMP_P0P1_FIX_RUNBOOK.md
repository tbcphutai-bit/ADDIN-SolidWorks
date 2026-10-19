# 22 — P0/P1 Code Fix: Auto Frame Joint PropertyManager

**Code owner:** ChatGPT. **Target branch:** `fix/auto-frame-joint-pmp-p0p1-20261019`  
**Base:** `review/antigravity-pmp-handoff-20261019`, commit `d4b0c10`.  
**Scope:** Three C# files only. No Check Hole, Bend, Mirror or TaskPane modifications.

## What changed — REAL C# code, not only a plan

| File | Exact changes |
| --- | --- |
| `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` | Persists last analysis status for unchanged A/B; clears status when selection changes; separates shallow selection callbacks from deep Part checks; fails closed on COM object identity uncertainty; refuses switched Assembly; wraps cleanup callbacks. |
| `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs` | New `AnalyzePair(ISldWorks, ModelDoc2 expectedAssembly, Component2 a, Component2 b)` overload checks original Assembly identity, refuses duplicate/indeterminate COM identity. Original overload remains as wrapper. |
| `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs` | Captures Assembly at command launch, calls overload with captured context, detects `ROLE: NEEDS_REVIEW` and `EQUAL_OR_AMBIGUOUS` explicitly; uses honest short status rather than implying successful analysis when unknown. |

**Unchanged:** `ADDIN.csproj` (Compile entries were already added in `f646c2a`), `SWAddin.cs`, `BomTaskPaneControl*`, `UI/MirrorAssemblyPropertyManagerPage.cs`, all other commands.

## How to apply via Antigravity

1. STOP any Copilot or Anti session editing the same files.
2. Read the whole report already pushed: `docs/AutoFrameJoint/ANTIGRAVITY_PMP_HANDOFF.md` and `docs/AutoFrameJoint/21_CHATGPT_REMOTE_CODE_REVIEW.md` (separate review branch).
3. Inspect own isolated worktree status **before any switching**:

```powershell
git rev-parse --show-toplevel
git branch --show-current
git status --short --untracked-files=all
git diff --stat
git log -1 --oneline
```

4. Work only in a NEW isolated clone or worktree checked out from `origin/fix/auto-frame-joint-pmp-p0p1-20261019`. Never overwrite dirty files from the original PMP or Copilot research folder. Do not merge to main or Check Hole.
5. Open and verify the exact three files above. **Do not regenerate callback signatures or change selection marks**. Required marks: `Frame A=1`, `Frame B=2`. Do not modify backend on your own.
6. Build with actual VS Developer PowerShell in the isolated folder:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" ".\ADDIN.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:minimal
Write-Host "BUILD_EXIT=$LASTEXITCODE"
```

7. On **any** error, record exact error code, file, line, SDK/interops and compiler output. If inside `Commands/**` or backend logic, report to ChatGPT and stop rather than self-patching. UI-only corrections should also be reported before pushing.
8. No host test can be marked PASS until a real launcher is connected and tested in SOLIDWORKS. No renderer/body preview or cutter is implemented at this point.
9. Save evidence locally then push **only a new review branch** with actual changes (if there are any) and report; no force-push.

## Manual host scenarios for NEXT iteration (after a separately reviewed launcher)
- A empty / B empty -> Analyze disabled; no stale result.
- A valid, B empty -> disabled.
- A and B exact same Component -> rejected.
- A and B two different loaded Part occurrences -> button enables and reports read-only estimate.
- Click Analyze without changing A/B -> result text MUST persist.
- Replace B with another Part -> old result disappears.
- A or B a subassembly or unresolved lightweight -> explanation when Analyze attempted.
- Switch active document -> reject, do not analyze a different Assembly.
- Nested component with same Name2 but different occurrence -> COM identity, **not name-only fallback**.
- Click Cancel / Esc -> page handler releases, cleanup callback errors must not crash process.
- No Feature tree edits, no save, no mirror edit, no Cut.

## Why we cannot call it proven yet
The code was authored via GitHub API, not compiled with the user’s installed SolidWorks Interop assemblies. Only Antigravity can run full `ADDIN.csproj` build and the user can test the native PMP host once the launcher is connected. The previous `Build PASS` was for **older commit `f646c2a`**, not this patch. `BUILD PASS` here remains **PENDING**.

## Official SOLIDWORKS API behavior used
- `swObjectEquality`: `swObjectNotSame=0`, `swObjectSame=1`, `swObjectUnsupported=2`. A returned 2 must NOT be treated as different.
  https://help.solidworks.com/2024/english/api/swconst/SolidWorks.Interop.swconst~SolidWorks.Interop.swconst.swObjectEquality.html
- `IPropertyManagerPage2Handler9.OnSelectionboxListChanged` occurs during selection processing; should only query, not modify model/selections:
  https://help.solidworks.com/2025/english/api/swpublishedapi/SOLIDWORKS.Interop.swpublished~SOLIDWORKS.Interop.swpublished.IPropertyManagerPage2Handler9~OnSelectionboxListChanged.html
