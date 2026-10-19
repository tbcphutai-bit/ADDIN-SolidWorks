# Specific C# Fix — NaN / Infinity input guard

**Status:** patch authored; NOT locally compiled in this ChatGPT environment. Requires Visual Studio / MSBuild real execution and report.

**Branch:** `fix/auto-frame-joint-finite-inputs-20261019`. Derived from documented Phase 1 baseline; no production Add-in files modified.

## Exactly what changed
- `tools/AutoFrameJointResearch/FrameJointGeometry.cs`: `IsFinite(double)` and `IsFinite(V3)`; input guards for tolerance, frame coordinates and normal; fail-safe `InvalidInput`; overflow guards for frame lengths and axis intersection. No SOLIDWORKS COM or Cut Feature.
- `tools/AutoFrameJointResearch/FrameJointNumericTests.cs`: 14 numeric guard assertions, including NaN/infinity and baseline valid geometry.
- `tools/AutoFrameJointResearch/AutoFrameJointResearch.csproj`: compile `FrameJointNumericTests.cs`.
- `tools/AutoFrameJointResearch/Program.cs`: run existing tests PLUS numeric guards; nonzero exit code on failure.

The original `FrameJointGeometryTests.cs` was **not** edited by this patch, to protect existing Copilot local test changes.

## To Copilot in Visual Studio
1. If still running, stop Agent. Confirm repo path/branch and `git status --short` **in the isolated AutoFrameJoint clone**, not the CheckHole solution.
2. **Preserve all local changes**. If there are uncommitted modifications, inspect and commit to a temporary user-approved WIP branch, or use a reviewed `git stash push -u` and later `git stash pop`. Never reset/discard.
3. Fetch branch `fix/auto-frame-joint-finite-inputs-20261019`. If your branch derives from `docs/auto-frame-joint-phase1-20261019`, cherry-pick the fix commit SHA provided to you, **after local changes are secured**. Do not merge to main.
4. Run in Developer PowerShell at repository root:

```powershell
msbuild .\tools\AutoFrameJointResearch\AutoFrameJointResearch.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
& .\tools\AutoFrameJointResearch\bin\Debug\AutoFrameJointResearch.exe --self-test
if ($LASTEXITCODE -ne 0) { throw "Self-test failed" }
```

5. If tests still fail, preserve exact failing assertion and latest source lines; edit only allowed research files and test again. Ensure no older stale EXE was executed.
6. Write `docs/AutoFrameJoint/COPILOT_EXECUTION_REPORT.md` with console output, exit code and files changed. No commit/push without user confirmation.

## Required PASS
- `NaN length tie tolerance rejected` must PASS.
- `NaN frame start rejected` must PASS.
- `infinite frame endpoint rejected` must PASS.
- `overflowing frame length rejected` must PASS.
- Existing geometry tests must still PASS.
- Exit code 0 (verified locally, not assumed).

## Important limitations
This is numeric input validation for a **pure geometry prototype**, not a fully adaptive profile-cut solver. KCC rule applicability and profile Web/Wall/Lip cut shapes remain research tasks. No Part is modified.
