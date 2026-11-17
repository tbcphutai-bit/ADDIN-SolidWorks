# CODEX HANDOFF — Custom quantity + equal-pitch on BOTH ends (2026-11-17)

## Repository and scope

- Repo: https://github.com/tbcphutai-bit/ADDIN-SolidWorks
- Original draft PR: #63; original development branch: `feature/circle-face-tube-layout-prototype-20261110`.
- New stacked branch: `feature/circle-face-custom-equal-both-ends-20261117`. Review this branch against the original development branch, NOT directly against `main` unless instructed.
- Scope is **only** CircleFaceTubeLayout. Preserve all other add-in commands and their behavior.
- Do NOT merge. First inspect and explain the existing solver, then build and test, and report code changes.

## User requirement — keep old logic, ADD a second mode

The operator selects **one face** of a curved/twisted sheet in SolidWorks 2024.
The system identifies the small and large boundary rails and computes the 3D tube axes.
It must support BOTH independent arrangements, chosen in the Custom count UI:

1. `TangentContact` (existing/default): on the small end adjacent tubes touch or satisfy the existing 3D contact tolerance. On the large end, tubes are distributed at equal pitch and balanced margins. Automatic count stays on this mode.
2. `EqualPitch` (NEW): for a **custom** tube count N, the small end is ALSO divided into equal arc-length stations and has **symmetric end margins**, exactly like the large end. Both ends have N stations and independent pitches. Tubes need **not** touch at the small end; do not reject merely for a nonzero small-end gap. Still check all full-length 3D cylinder collisions and sheet interference.

User may change N (e.g. 20, 30, 40, 41) and diameter (default 20 mm). Preserve Add +1, Flip Side, Preview, Create 3D Sketch, automatic debug logs. The old `TangentContact` mode is unchanged as the default.

## Current code already on this branch

- `Commands/CircleFaceTubeLayout/TubeLayoutSolver.cs`: `SmallEndSpacingMode`, `SolveEqualStations`, symmetric cap-margin correction, `VerifyExact` on the short rail, and mode-aware full-cylinder collision logic.
- `Commands/CircleFaceTubeLayout/SwFaceLayout.cs`: mode threaded through solver. Auto count is still contact-only.
- `Commands/CircleFaceTubeLayout/CircleFaceTubeLayoutForm.cs`: adds a small-end mode selector; enabled for Custom count.
- `Commands/CircleFaceTubeLayout/CircleFaceTubeLayoutCommand.cs`: mode passed from form to face analyzer.
- `Tests/CircleFaceTubeLayout/GeometryTests.cs`: new `TestEqualPitchCustom` with symmetric margins, N=20, and dense N=41 case.

This is a source-level change only; **it has not yet been compiled or validated in SolidWorks**. Do not call it finished until validation.

## Correct geometry and formulas

Let `Ls`, `Ll` be actual short/long boundary *arc lengths*, N the selected count, D the **outside** tube diameter, M the intended end-cap margin (mm, user setting). Define 3D axis endpoints using the face normal/Flip Side exactly as the current code; do not substitute a single Circular Pattern or a common angle.

For a simple 2D estimate before 3D corrections:

- Short end center station first = D/2 + M
- Long end center station first = D/2 + M
- Short pitch = (Ls - D - 2M)/(N - 1)
- Long pitch = (Ll - D - 2M)/(N - 1)

These pitches are measured along their respective arc lengths; they are *not* the same pitch. For **tilted finite cylinders** the projected end-cap extent differs. Iterate the first/last station offsets so the true projected **end-cap clearances** are symmetric and match M within tolerance. Inverse image/UV transforms must use SolidWorks meters internally.

The resulting candidate axes connect matched points (index i at short end to index i at long end). Check 3D full-cylinder separation (not just station distance or circles projected on a view), face clearances, and actual sketch-end geometry.

Example from user's actual curved rails:
- Small: Ls = 811.35 mm (R ~3247 mm)
- Large: Ll = 1421.97 mm (R ~4323.16 mm)
- D=20 mm, N=30, M=5 mm:
  - short nominal pitch = 781.35 / 29 = 26.943103 mm
  - long nominal pitch = 1391.97 / 29 = 47.998966 mm
- N=41, M=5 yields short nominal pitch=19.53375 mm (<20), which can produce 3D interference; **never silently force a clash-free result**. Explain that user must reduce N, D or margin, or later choose a supported overhang mode. There is no automatic extrapolation beyond rail endpoints in this version.

## Required review and hardening

1. Review branch diff; compile SolidWorks add-in (x64, .NET Framework 4.8, SOLIDWORKS 2024 interop).
2. Run geometry tests and add regression tests for N=20, 30, 40, 41, with flat + twisted, Flip Side, insufficient length, and negative/zero clearance corner cases.
3. Confirm `EqualPitch` does not call/enforce the small-end contact tolerance. But it MUST preserve all full-length pair-wise 3D cylinder and sheet checks.
4. Confirm `TangentContact` old tests and auto count still pass unchanged.
5. Confirm small and large projected cap margins are equal left/right within existing `SymmetryTolerance` and reported in Preview and log.
6. Check UI layout in SolidWorks (scaling, translations, display values, “Auto count” disables small-end selector, changing modes invalidates preview, Create is disabled for any issue).
7. Verify PREVIEW remains non-destructive: only on explicit Create do we generate 3D Sketch. Cancel/Undo cleans up. Do not create tube solids.
8. Analyze if all pair collisions are properly handled when GJK bounds are inconclusive; fail safely, never produce verified results without proof.
9. Test real model `4313-04-HU-H1.SLDPRT` on user's machine (use local copy, not source-controlled binary); log the face reference, short/long rail lengths, N/D/M, station coordinates, actual 3D pairwise minimum gaps, sheet clearance, and any rejected geometry.
10. Fix any compile/runtime/regression issues. Avoid changing unrelated commands, task pane event handlers, BOM, Check Drawing, Check Same Part, or Check Hole.

## UX acceptance

**Auto count** => existing contact mode only.

**Custom N** => operator can choose:
- `Touch at small end (existing)`
- `Equal pitch at both ends`

Example successful Preview:
`Small end: pitch X mm, cap margins L/R = ...; Large end: pitch Y mm, cap margins L/R = ...; N total; min full 3D gap; sheet clearance; OK/INFEASIBLE`.

For infeasible geometry, provide a concrete reason and keep **Create 3D Sketch** disabled. Never make solid tubes as part of this feature.

## Report requested from Codex

Commit to the stacked branch only; do NOT merge. Provide:
- Summary of old vs new behavior
- Exact changed paths, commit SHA and build/test results
- What 3D cases were actually tested vs not tested
- Debug log location and any failed cases
- Remaining limitations (real face selection, twisting, small margin, unsupported overhang)
- Proposed final fix if the solver cannot meet constraints, explaining feasibility instead of silently relaxing them
