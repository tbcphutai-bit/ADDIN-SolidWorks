# INDEPENDENT REVIEW HANDOFF — Circle Along Face V3, PR #65
Review date: 2026-11-18
Repository: https://github.com/tbcphutai-bit/ADDIN-SolidWorks
Branch: `feature/circle-face-v3-smart-overhang-20261117`
PR #65: https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/65
Anti code commit: `9ad5e62b6f33d59198409674e858990d41ad0e77`
Anti report: `Prototypes/CircleFaceTubeLayout/V3_IMPLEMENTATION_REPORT.md`

## Observed evidence (report claims, not independently re-run by ChatGPT)
- C# 7.3 /warnaserror geometry tests, ADDIN Debug|x64 and Release|x64 builds reported exit 0 on SolidWorks 2024 SP5.0.
- Real Part `4313` (φ20 / M=5 / Hmax=12): N30 keeps margin 5, N40 H=2, N41 H=12, with N41 max center extrapolation 2.248 mm, min full-cylinder gap **0.0234 mm**, sheet BREP collisions 0.
- Real surface `6866`: N41 H=9 (normal) / H=11.5 (flipped). All successful cases checked against selected owner sheet and other generated cylinders in report.
- Preview/Cancel/Create N41 3D axes/Undo and assembly coordinate transform reported PASS.
- Report's assembly audit of 57 components in `copy_4316.SLDASM` found **unaddressed neighboring solid-component intersections**:
  * N30/H0: axis 30 collides with `4316-04-KDA1-o1-1`;
  * N40/H2: axis 40 collides;
  * N41/H12: axes 40 and 41 collide.
- Local logs listed in report are on Anti's machine only; not directly available here.

## Independent review assessment and priority
P0 — **Assembly collision omission.** `SwFaceLayout.CheckSheet` checks `ownerBody` or selected face sheet only, plus tube-tube GJK. It does not check surrounding assembly components or other independent bodies. Thus a green Preview does NOT demonstrate assembly-wide freedom from collision. First resolve by explicitly labelling preview scope and preferably add optional (or default) comprehensive assembly-component BREP validation before enabling a claimed "verified assembly-clear" result. Use component transforms and transient copied bodies, never modify model originals. Regression must reproduce the known `4316-04-KDA1-o1-1` collisions at N=30/40/41. Discuss whether the user wants to block Create (only 3D construction axes) or allow a warning; never silently show "fully clear."

P1 — **Engineering clearance policy.** N41/4313 min full-cylinder gap is reported 0.0234 mm, and 6866 N41 is around 0.0138–0.0212 mm. This is geometry-model separation only; it does not establish realistic fabrication clearance or tolerance stack-up. Propose configurable manufacturing clearance (and optionally a separate tangent-contact rule). Do not equate GJK resolution 0.0002 mm with acceptable practical clearance.

P2 — **"Minimum overhang" means first PASS on 0.5 mm grid, not true global minimum.** Report actual step size and optionally run local refinement after the first passing H, with all collision checks preserved. Do not claim globally minimal overhang without monotonicity proof.

P2 — **Early `flipped_solid_material_clash` pruning needs proof.** Current `Analyze()` stops at first pair-clear candidate when all N tubes intersect the owner solid. It is a useful optimization for the tested flipped thick solid, but `allN clash at H` does not logically demonstrate `all H'>H clash` on arbitrary twisted faces. Test counterexamples and either prove a conservative bound or turn this into a user-visible guarded heuristic that never labels the entire remaining H range mathematically infeasible.

P2 — **Tangency scope.** Straight axes are built from offset normals at short/long ends, then body clearances are verified; the algorithm does NOT guarantee continuous tangency between rigid tubes and the entire twisted surface. State this clearly in UI/doc/acceptance.

## Codex requested workflow
1. Read full Anti report and all PR #65 changed files, inspect `SwFaceLayout.Analyze`, `AnalyzeOne`, `CheckSheet`, `ExtendRail`, `TubeLayoutSolver`, `TubeDistance`, and UI.
2. Independently verify builds and tests (where SolidWorks 2024 available); record command, environment, exit code. Request Anti's original log files if detailed validation needed; do not claim to have read local logs from report alone.
3. Specifically reproduce `4316` assembly clashes. Design scoped and safe assembly context validation (or explicit warning and restrictive UI behavior), and assess cross-component transformations and other bodies.
4. Quantify realistic minimum clearance and suggest acceptance threshold; preserve GJK/BREP numerical soundness.
5. Recheck V2 regressions: Auto/TangentContact; Custom EqualPitch; Add +1; Flip; Cancel; 3D Sketch construction-only; Undo; invalidation and renderer.
6. Keep V2 PR #64 and V3 PR #65 unmerged; do not alter unrelated add-in commands. Changes should be isolated to CircleFaceTubeLayout and tests/docs unless explicitly approved.
7. Write `Prototypes/CircleFaceTubeLayout/CODEX_V3_REVIEW_REPORT.md` with severity-ranked findings, actual tests, remaining limitations and recommended next release decision. No unsupported "production ready" claim.

## Review acceptance
- No ambiguity between 'clear against selected body' vs 'clear across full assembly'.
- In assembly, known neighboring collisions must be detected/reported (or clearly flagged unverified), even if only sketch axes are being created.
- No silent count changes, no silent extra overhang, no skipped collision checks on accepted candidates.
- Normal 30-tube baseline remains margin 5 mm with H=0.
- 40/41 cases supported only when proven geometry-valid with explicit overhang option.
- Do not merge until reviewer records the assembly collision and clearance policy disposition.
