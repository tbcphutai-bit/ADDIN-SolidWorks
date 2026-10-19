# 08 — COPILOT IMPLEMENTATION RUNBOOK (Visual Studio; executable research code)

> Goal: Copilot has explicit files, exact commands, verification gates and output reports. **Do not invent successful results.** This guide is for an **isolated research executable**; it does NOT implement a cutting command yet.

## 0. READ ALL OF THIS FIRST
1. Checkout `docs/auto-frame-joint-phase1-20261019` in a separate clone/worktree. Ensure `git status --short` is clean or understand uncommitted work.
2. Read `docs/AutoFrameJoint/README.md` + 01–07, then this guide and 09–10.
3. Inspect existing repo `ADDIN.csproj` and `SWAddin.cs` (only read).
   - Existing Add-in: **.NET Framework 4.8**, old-style non-SDK csproj, **C# 7.3 on x64 configuration**, SOLIDWORKS COM references.
   - Current `SwAddin` uses an existing `BomTaskPaneControl`. Do not modify it here.
4. Inspect `tools/AutoFrameJointResearch/`:
   - `AutoFrameJointResearch.csproj`: independent .NET Framework 4.8 Console .exe, no SW APIs.
   - `FrameJointGeometry.cs`: exact pure geometry input/output and finite-axis intersection.
   - `FrameJointGeometryTests.cs`: executable assertions.
   - `Program.cs`: entry point and exit code.

## 1. RUN EXACTLY THIS BUILD/TEST CYCLE
Using **Developer PowerShell for Visual Studio** from repo root:
```powershell
git status --short
msbuild .\tools\AutoFrameJointResearch\AutoFrameJointResearch.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU /v:minimal
if ($LASTEXITCODE -ne 0) { throw "AutoFrameJointResearch build failed" }
& .\tools\AutoFrameJointResearch\bin\Debug\AutoFrameJointResearch.exe --self-test
if ($LASTEXITCODE -ne 0) { throw "AutoFrameJointResearch self-test failed" }
git diff --stat
```
If `msbuild` is not on PATH, use VS Developer PowerShell or inspect installed MSBuild via `vswhere.exe`. Do **not** quietly switch the main Add-in to .NET 8, change `ADDIN.csproj`, or claim that build passed without the tool output.

## 2. WHAT THE CODE DOES — AND DOES NOT DO
- Input: two finite axis spans **already extracted** and expressed in the **same millimetre coordinate system**, a panel normal, explicit tolerances, and rule applicability.
- Output: measured lengths, long/short, acute angle between **axes**, finite-line intersection, winner/loser under precedence, and a reason/status.
- Winner precedence: Drawing override > KCC 26003 only when scope Applicable > square convention for equal length; otherwise Undetermined.
- **Cut candidate is intentionally disabled** by `CanAutomaticallyChooseCut=false`.
- This starter code does **not** extract 3D B-rep; cannot read a STEP; does not infer 'inside/outside'; does not define interior angle; does not produce cut surfaces; does not assert true geometry of provided reference models.
- Axis spans are **not** production-grade frame-axis detection. A separate SOLIDWORKS read-only adapter/analyzer is required later.
- Frame geometry may be offset: centreline/axis intersection outside the supplied spans can be valid for real corner design; current status says `OutsideSegments` and requests review, not 'invalid joint'.

## 3. REQUIRED COPILOT TASKS, IN ORDER
**Task A — compile-only diagnostic:** open `AutoFrameJointResearch.csproj`, build, capture full compiler output. Fix only compiler errors in isolated research files. Rebuild.

**Task B — self tests:** run exe, attach console exit code/output. Never count a test as PASS unless run. Add tests for:
- swapping A/B reverses roles/winner but not physical intersection;
- changing sign of frame axis does not change acute angle or lengths;
- mirrored geometry preserves geometric quantities;
- Drawing override also works if KCC scope is NotApplicable;
- invalid inputs and non-coplanar axes return safe statuses.

**Task C — geometry audit:** inspect `FrameJointGeometry.cs` for:
- normal/direction normalization, length units,
- near-parallel determinant handling,
- line intersection equations,
- exception risks for degenerate vectors,
- false positive `Analyzed` when trim geometry cannot be chosen.
For each concern, add a failing test FIRST and apply the smallest code change. Rebuild/retest.

**Task D — architectural review:** add a Markdown report `docs/AutoFrameJoint/COPILOT_EXECUTION_REPORT.md` with exact build command, VS version, changed files, pass count, exceptions, and TODOs. Keep source changes restricted to `tools/AutoFrameJointResearch/**` and reports under `docs/AutoFrameJoint/**`.

**Task E — commit:** after test PASS and human consent, use:
```powershell
git status --short
git add tools/AutoFrameJointResearch docs/AutoFrameJoint/COPILOT_EXECUTION_REPORT.md
git diff --cached --stat
git commit -m "test(auto-frame-joint): validate phase1 geometry research kernel"
git push -u origin HEAD
```
Do **not** merge to `main`; do **not** stage unrelated source changes. If tooling/permissions prevent build, report BLOCKED and exact missing capability; no fabricated compile/test result.

## 4. BOUNDARIES FOR PHASE 1
Forbidden:
- editing `ADDIN.csproj`, `SWAddin.cs`, `BomTaskPaneControl*`, `Commands/**`, `UI/**`, existing tests unrelated to AFJ,
- invoking `Cut-Extrude`, `Cut with Surface`, `Combine`, `Save` on CAD files,
- changing Check Hole or Bend Check,
- uploading internal `SLDPRT`/`SLDASM`/`STEP`/`PDF` samples to a public repo.

Allowed only in **separate Phase 2 approval**: a read-only SOLIDWORKS adapter inside the Add-in, after verification that the geometry research code compiles and tests run.

## 5. REQUIRED REPORT FORMAT
```markdown
# Auto Frame Joint — Copilot Execution Report
- Branch:
- Baseline commit:
- Compiler/VS/MSBuild version:
- Commands actually executed:
- Build result (PASS/FAIL/BLOCKED):
- Self-test exit code / assertions:
- Files changed:
- Geometry assumptions reviewed:
- Problems corrected (test-first):
- Known unknowns / cannot debug automatically:
- Read-only SW integration readiness:
- Commit SHA:
```
