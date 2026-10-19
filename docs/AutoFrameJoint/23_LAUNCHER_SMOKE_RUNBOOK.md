# 23 — Auto Frame Joint native PMP launcher (Host Smoke Test Only)

Code owner: ChatGPT. Parent: `edce4a555d1408d96a722b54668d60d9671ef14b`.
Branch: `feat/auto-frame-joint-host-launcher-smoke-20261019`.
**Status: code authored, NOT locally built/tested in SOLIDWORKS.**

## Purpose
Expose a SINGLE `Giao góc Frame` tile in **Model → Macro**, so a tester can open the native PMP and test Frame A/B selection. This feature is READ ONLY: no temp-body preview, no cutting, saving, or geometry modifications.

## Exact code files written by ChatGPT
- `UI/AutoFrameJointTaskPaneLauncher.cs` (NEW): `partial class BomTaskPaneControl`, creates a single `ModernButton` with `TaiIcon.Assembly`; calls `AutoFrameJointCommandEntry.Open(swApp)` through the existing `CanRunModelMacro` permission check.
- `UI/BomCommandLayout.cs`: EXACT 3 small edits:
  1. `EnsureAutoFrameJointButton()` immediately after `EnsureBendMinimumButton()` in `EnsureModelMacroControls`;
  2. add `btnAutoFrameJoint` to `MoveControlToCard` loop;
  3. add `btnAutoFrameJoint` to `LayoutModelMacroViewport` tiles array.
- `BomTaskPaneControl.cs`: EXACT 2 small edits: only allow tile in `swDocASSEMBLY` in `IsModelMacroSupported`; update button in `UpdateModelMacroCommandAvailability`.
- `ADDIN.csproj`: 1 new `Compile Include="UI\AutoFrameJointTaskPaneLauncher.cs"` directive. Existing P0/P1 files and Compile directives retained.
- **Intentionally unchanged:** `SWAddin.cs`, `BomTaskPaneControl.Designer.cs`, Check Hole/Bend/Mirror classes, geometry analyzer and PMP callbacks.

## What Antigravity must do now
1. STOP other agents editing the same worktree, preserve any uncommitted WIP. Work in a NEW isolated clone or worktree at exactly this branch's commit; do NOT checkout atop a dirty worktree.
2. Read real C# code in all four changed paths before build. Verify visual tile is added ONCE and the macro layout includes it both in card and size calculations. **Do NOT design a second PMP or modify its handlers.**
3. Run a build using Visual Studio 2026 Developer PowerShell. Critical: the current project has a PostBuild event that copies DLL and PDB to `C:\Tao moi\ADDIN.dll` and `C:\Tao moi\ADDIN.pdb`. Avoid overwriting the existing installed Add-in during this smoke-build by overriding `PostBuildEvent` as an empty global property:
```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild ".\ADDIN.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 '/p:PostBuildEvent=' /v:minimal
Write-Host "BUILD_EXIT=$LASTEXITCODE"
```
Double-check the evaluated PostBuild behavior and actual output path before invoking. If the override does not disable the copy, STOP and propose an isolated build method; do not overwrite the installed DLL. Do not modify `ADDIN.csproj` PostBuildEvent to trick the build.
4. If build FAILS, submit exact compiler errors and code/file lines, stop. UI-only issues may be proposed; backend and project changes require ChatGPT review.
5. If Build PASS, report actual assembly output path, size, SHA256 hash, compiler logs and exit code. Do NOT report host PASS. Upload a report to a new review-only branch after user's approval.
6. Host Smoke Test is run **only after explicit user consent** to back up/replace the test add-in DLL and launch in SOLIDWORKS. Keep original Add-in DLL and PDB backed up, avoid changing production registration in this step. Test in a COPY of a real Assembly.
7. Keep test cases:
   - No Assembly -> button disabled. Assembly -> enabled.
   - Open PMP and choose A, then B. Marks 1 and 2 each hold 1 Component.
   - Same occurrence A/B -> rejected.
   - Click Analyze -> UI says `Đã ước lượng dài/ngắn` or `Cần kiểm tra...`; no status overwrite.
   - Swap/replace B -> old analysis status clears.
   - Switch active Assembly -> reject / close and reopen.
   - Cancel/Esc -> no stale page or changes.
   - Repeat opening twice -> no duplicate PMP or exceptions.
   - Feature Tree, Mate status, Component configurations and document dirty state unchanged.
8. After test, submit **raw log, screencaps** without confidential Part filenames; then ChatGPT inspects exact post-test commit/diff. No merging to main or any other feature.

## Review acceptance before any Preview3D work
- Exactly 1 new tile; visible and correctly laid out narrow/wide.
- Branch-specific build with no copy into global installed DLL.
- No source changes beyond 4 authorized files + documentation.
- Real SOLIDWORKS PMP opens with distinct A/B selection boxes; analyzes read-only; Cancel works.
- No real cutter/preview feature is wired at this phase.
- No automatic merge.

## Code-owner concerns to watch
The P0/P1 selection callback uses `app.IsSame`; unknown COM identity fails closed. `GetModelDoc2()` only occurs on Analyze. Results may be provisional. Component name/path/config are not proof that a chosen geometry axis is correct.

## Command (copy to Antigravity)
Read commit and this runbook. Do not regenerate source. Inspect files, run isolated Rebuild with PostBuild copy disabled, show exact build evidence and changed-file report. Stop on any errors. Ask before SOLIDWORKS host activation. No changes to Check Hole/Bend/Mirror/other backend.
