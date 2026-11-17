# CODEX REVIEW — After Anti V2 (2026-11-17)

Repo: https://github.com/tbcphutai-bit/ADDIN-SolidWorks
Branch: `feature/circle-face-custom-equal-both-ends-20261117`
PR: https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/64 (Draft; stacked on #63)
Anti implementation commit: `eb9d9e6f80b16fa9246e51865e01e09a3f8864bf`
Anti report commit: `22413396a23264d240aafa142eeffbc36e3c6c7e`
Report: `Prototypes/CircleFaceTubeLayout/ANTI_IMPLEMENTATION_REPORT.md`

## Role: independent technical reviewer, not wholesale re-implementation

Read the entire Anti report and the latest branch HEAD, source files, tests, and PR diff. Distinguish directly reproducible observations from statements in Anti's report. Check all tests on a machine with SolidWorks 2024 SP5.0 where possible and avoid inferring proof without execution. Keep PR Draft, do not merge into main, do not modify unrelated add-in commands.

## Product goal

Select one curved/twisted sheet face -> auto-identify short/long end rails -> default φ20 tubes -> choose Auto/Custom count; in Custom choose TangentContact (old, short-end touching) or EqualPitch (both rails independently spaced), plus Add +1, Flip Side, cap margin, non-destructive Preview, validated Create 3D Sketch axes only, automatic debug log. User allows slight overhang at both ends if truly necessary, but any such behavior must be an **explicit, separately reviewed option**, never added silently.

## Anti-reported evidence to audit

- Pure Geometry Tests: `PASS all geometry tests` with C# 7.3 x64 warn-as-error.
- ADDIN.csproj Debug|x64 and Release|x64 reported successful with SOLIDWORKS 2024 interop.
- Direct COM integration on copied Part files (not final Task Pane user-click workflow):
  * `4313-04-HU-H1.SLDPRT` solid, measured rail lengths 816.5458 mm / 1428.8246 mm. EqualPitch N=20,30,39 with D=20 M=5 Flip=false: verified by Anti; N=40 collided (21 interfering pairs, first 1-2, reported penetration -0.3289 mm); N=41 collided (40 pairs). TangentContact N=40,41 reported OK.
  * `6866-04-円錐架台-上型-3d 133333.SLDPRT` surface, 817.5470 / 1430.1414 mm. EqualPitch N=20,30,39 D=20 M=5 reported OK both sides; N=40 M=0 Flip=false reported OK; N=40/41 M=5 reported blocked.
  * 3D Sketch N=30 creation, Cancel, Undo and debug paths tested in integration harness.
  * Solid-body Flip can point into metal; appropriately blocked.
- Anti debug paths are local to Anti's computer, not stored in GitHub. Request log files for independent investigation if necessary. Do not claim to have inspected them just because report lists their paths.
- GUI clicking from actual Task Pane and new Assembly-context runtime are not verified in Anti's latest V2 report.

## Mandatory review plan and expected analysis

1. **Numerical geometry audit**:
   - Inspect `TubeLayoutSolver.SolveCore/SolveEqualStations/VerifyExact/VerifyTubes`; confirm EqualPitch does not enforce contact; cap-margin correction converges, handles degenerate/overly short rails and maintains station ordering.
   - Independently validate GJK `TubeDistance.Between` finite-cylinder support, simplex behavior, lower/upper distance bound interpretation, tolerance units, uncertainty fail-safe. Compare carefully against SolidWorks B-rep for near-touch cases.
   - Check face normal, Flip Side, body/face transformations, coedge parameter reversal and curve-length inversion.
   - Verify `Create 3D Sketch` count and what it actually guarantees. Differentiate *axes locally offset by face normals at ends* from *true tangency/contact along full length of rigid cylinders with a twisted surface*. Do not claim continuous tangency unless specifically measured.
2. **Compatibility, UI, regressions**: re-run Debug/Release x64 builds; run old and new geometry tests (flat, twisted, N20/30/39/40/41, both flips, custom +1, zero/positive margins), exercise actual SolidWorks Task Pane (not just direct COM dialog), Preview/Cancel/Undo, and Assembly component-transform path; ensure unrelated commands unchanged.
3. **Feasibility vs requested count**:
   - Confirm that N=40/41 M=5 EqualPitch failures on the *specific 4313 solid* are physical B-rep collisions, not simply conservative/GJK false alarms. Provide pair positions, penetration and tube-angle diagnosis.
   - Do NOT arbitrarily force 40/41 or loosen collision thresholds, and do not silently choose N=39.
   - User previously explicitly allowed **slight overhang on both ends**. Evaluate (as a proposed *next version*, not auto-implement without review) whether explicit per-end overhang/negative edge-margin, supported rail extension, variable edge clearance, slight axis-angle optimization, or smaller diameter can achieve requested N without full-length collision. An extension beyond the selected face boundary is *not* directly available from the trimmed rail; document geometric continuation strategy and limits. Keep the selected mode and symmetric left/right condition; evaluate actual 3D feasibility for each model.
   - If impossible, report quantified minimum required overhang, tube diameter reduction, or maximal verified count, making clear any extrapolation and preserving nonintersection.
4. **Security/data preservation and debugging**: source Part untouched, no modification without Create; preserve current command and no crashes or COM leaks. Request Anti's two debug logs if branch+report are insufficient.
5. Output an independent `CODEX_REVIEW_REPORT.md` under `Prototypes/CircleFaceTubeLayout/`: findings ranked by severity, reproducible test evidence, changed code paths if any, build/test commands/results, verified and unverified claims, feasibility options, UI screenshots/log locations, recommendations and acceptance checklist.
6. Commit **only** necessary CircleFaceTubeLayout fixes/docs to same feature branch and update PR #64; do NOT merge and do not touch main. If changes are unnecessary, submit review only.

## Definition of done

Both modes preserved, model selection still one face, small/large spacing by true arc length, explicit and accurate 3D collision detection, trustworthy UI state & logs, genuine SolidWorks 2024 validation, and separately documented next-step overhang option if feasible.