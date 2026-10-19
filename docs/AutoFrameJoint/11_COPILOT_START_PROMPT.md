# 11 — Paste this entire instruction into GitHub Copilot Agent (Visual Studio)

## Prompt
You are responsible for executing **Phase 1 Auto Frame Joint geometry research** for this SOLIDWORKS C# add-in. Treat the checked-out GitHub Markdown as the authority.

Repository: `tbcphutai-bit/ADDIN-SolidWorks`  
Required branch: `docs/auto-frame-joint-phase1-20261019` (or your own child branch explicitly based on it).  
Code under test: `tools/AutoFrameJointResearch` (standalone net48 / C# 7.3).

### Mandatory order — do not skip
**Step 1.** Confirm repo name, branch and clean/dirty git status. **DO NOT discard changes or switch away if it would lose user work.**  
**Step 2.** Read every Markdown `docs/AutoFrameJoint/README.md`, `01_...` through `10_...`, and all files under `tools/AutoFrameJointResearch/`. State what was actually read.  
**Step 3.** Confirm that current Add-in uses net48 and C# 7.3 for x64 by reading `ADDIN.csproj`. Never modify project production files.  
**Step 4.** Run actual isolated build:
```powershell
msbuild .\tools\AutoFrameJointResearch\AutoFrameJointResearch.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU /v:minimal
```
If it fails, use compiler diagnostics to fix only files in `tools/AutoFrameJointResearch/` and rebuild.
**Step 5.** Run:
```powershell
& .\tools\AutoFrameJointResearch\bin\Debug\AutoFrameJointResearch.exe --self-test
```
Require exit code 0; do not infer tests passed from code inspection.
**Step 6.** Add regression tests for A/B selection swap, axis direction reversal, mirrored XY setup, explicit Drawing override and invalid tolerance. If any test fails, fix test-first and run complete suite again.
**Step 7.** Review code for incorrect corner/axis geometry assumptions. Report openly that the finite-axis-intersection is NOT the actual frame cut, and that KCC Winner/Loser does NOT solve Web/Lip/Bend cutting.
**Step 8.** Write `docs/AutoFrameJoint/COPILOT_EXECUTION_REPORT.md` with exact steps, output, pass counts, status, files changed, next open questions, Git branch and SHA.
**Step 9.** Present `git diff --stat`, state tests pass/fail and ask user whether to commit and push. **Do not commit without user confirmation.** Never merge or modify main.

### Allowed changes
Only research code `tools/AutoFrameJointResearch/**` and the requested execution report under `docs/AutoFrameJoint/**`. Do not touch production Commands, CheckHole, Addin.csproj, SwAddin.cs, TaskPane, UI or existing tests. Do not use SolidWorks COM in this first coding checkpoint.

### Debug contract
Loop build -> error -> minimal edit -> full rebuild -> full self-test. After 5 attempts without a fix, report exact BLOCKED state; never make claims of success without tool evidence. If an API/tool is inaccessible, state what permission or manual action is needed, do not invent logs or return fabricated PASS.

### Acceptance
A reproducible successful local build and self-test with log/exit code; strict non-mutating scope; a clear report of confirmed vs not-yet-proven geometry rules. No implementation of Cut Feature and no false claim of solving arbitrary frame profiles.
