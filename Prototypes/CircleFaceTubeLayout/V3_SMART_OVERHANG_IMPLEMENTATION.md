# CIRCLE ALONG FACE V3 — SMART SYMMETRIC OVERHANG

Date: 2026-11-17
Base: PR #64 (`feature/circle-face-custom-equal-both-ends-20261117`)
Branch: `feature/circle-face-v3-smart-overhang-20261117`

## User goal
Choose one curved/twisted face, Custom N of straight φD tubes, EqualPitch on both ends. Default margin 5 mm. Opt in to a bounded **Symmetric Overhang** only if standard equal-pitch layout for chosen N fails. Preserve exact N, independent arc-length station pitches and equal left/right projected end-cap margins on both ends. Create only 3D construction-axis sketch after validated preview. Retain Add +1 and Flip Side.

## Source implementation now present
- `TubeLayoutSolver.cs`: `Solve(...allowOverhang=false)` is backward compatible, permits negative target cap clearance only in `EqualPitch` and `allowOverhang:true`, limits cap protrusion to diameter, and conditionally unclamps station correction to `-radius`. Original Auto/TangentContact remains unchanged.
- `SwFaceLayout.cs`: `Analyze(...allowSymmetricOverhang,maxOverhang)` tries original positive-margin solution **first**, returns it immediately if verified. Only if infeasible, evaluates symmetric cap targets `0, -0.5, -1, ... -maxOverhang` mm and chooses the first candidate with no unproven tube-tube separation or sheet BREP intersection. All geometry in meters. Stations outside trimmed rails use bounded linear tangent extension and frozen endpoint normal. Any solver exception is logged. No unbounded extrapolation.
- `CircleFaceTubeLayoutForm.cs`: opt-in checkbox and maximum cap protrusion NumericUpDown (default 12 mm, bounded by D). Only enabled Custom+EqualPitch. Preview reports cap H, maximum center extension and whether extrapolated. Changing fields invalidates preview. Create stays locked for `Issue != null`.
- `CircleFaceTubeLayoutCommand.cs`: new options flow through command to geometry analyzer and debug.
- `GeometryTests.cs`: pure geometry scenarios N30 baseline; N40 H2 and N41 H12 with sloped synthetic rails; disallowed negative margins without opt-in.

## Terms: important!
"Cap overhang" H = projected outer cap protrusion beyond **each boundary endpoint** (target clearance `-H`). It is NOT necessarily the extension of the tube center. When D=20 mm and H=12 mm, the center may extend ~2 mm beyond the curve. `capOverhang` is symmetric on both ends and both left/right sides. Because 3D tubes are tilted, true center extension is obtained from the actual solver positions, not simply H - D/2.

"Small-first/large-first station" is equal arc length along each independent boundary; different pitches are intended. When beyond the trimmed rail, use tangent extrapolation only up to radius D/2 of center extension. The actual face does not magically extend: a simulated axis outside face bounds requires body and assembly clash verification.

## Critical verification not yet performed
The source code was edited via GitHub. **No Windows MSBuild, SOLIDWORKS interop or real GUI execution happened in this ChatGPT turn**. The draft PR must NOT be merged or called production safe until independent Windows x64 SOLIDWORKS 2024 SP5 tests.

### Required Anti/Codex build and test
1. Check out this branch. Run pure GeometryTests (C# 7.3 /warnaserror x64) and Debug/Release x64 .NET 4.8 Add-in builds using installed SOLIDWORKS interop.
2. Compare branch against PR #64; change no unrelated command or registration code.
3. Exercise user-selected **30 tubes** with `EqualPitch`, D20 M5 and SmartOverhang enabled: must keep original 5-mm cap margins; H=0; zero extrapolation; same 3D axes as V2.
4. Exercise 40 and 41 tubes on copied `4313-04-HU-H1.SLDPRT` (solid) and `6866...SLDPRT` (surface): with SmartOverhang OFF retain V2 INFEASIBLE; with SmartOverhang ON Hmax12 or more (but <=D) search first validated result. Compare anti/Codex V2 OverhangProbe approximate thresholds H2 (N40), H12 (N41 on 4313) and H9 (N41 on 6866), never hardcode these values.
5. Independently compare all near-contact GJK candidates to SolidWorks BREP, especially when negative cap clearance and `center_extrapolated=true`. Inspect adjacent flange/side walls and **other Assembly components** separately; current source's `CheckSheet` only checks the selected part's ownerBody/selected face, not the entire assembly.
6. Test false/true Flip Side, non-straight and split curves, edge-domain fallback, trimmed vs extrapolated normals, surface/solid and assembly transforms, Delete/Undo and Cancel, changing inputs before Preview completes. Test model with left/right asymmetric geometry.
7. Performance: searching multiple H values may require repeated BREP cylinder boolean operations. Measure elapsed time for N40/41 and optimize only with safe lower-bound rejection; do not bypass BREP on a potential successful candidate.
8. Confirm **no sketch is created by Preview**, only after explicit Create, and Create stays disabled for every GJK uncertain or BREP inconclusive candidate. Original model untouched in failure/cancel.
9. Log `SMART_OVERHANG_TRIAL`, `SMART_OVERHANG_RESULT`, `OVERHANG_EXTRAPOLATED`, `SMART_OVERHANG_ACCEPT`, full axis positions and `SHEET`/BREP results in `%LOCALAPPDATA%\TAI_TOOL\CircleFaceTubeLayout\`.
10. Report true tube-to-sheet tangency carefully: V3 only connects normal-offset endpoints and verifies no detected intersections; it does not make a straight tube continuously tangent to an arbitrary twisted surface.

## Acceptance checklist
- Default checkbox OFF so V2 output is bitwise/within solver tolerances unchanged.
- 30 tubes with checkbox ON: no overhang, old margin and original count preserved.
- 40/41: exact quantity or explicit INFEASIBLE, never auto-reduced.
- Overhang H increases until first verified candidate within Hmax, or explicitly fails.
- Both ends: independent equal-pitch and symmetric actual cap margins.
- No extrapolation beyond cap H<=D and center extension <= D/2.
- No GJK uncertainty or BREP clash approved.
- Preview/Clear/Cancel/Undo verified on SOLIDWORKS.
- No changes outside command folder/tests/docs unless absolutely necessary and reviewed.
- PR stays Draft; do not merge.

## Implementation notes to review
The solver's current `SolveCore` uses an iterative `CapClearance` projection and returns a `TubeLayout` with `Issue`; the face analyzer must not interpret `layout != null` as success. Only `layout.Issue == null` after exact verification and BREP passes is acceptable.
The `SwFaceLayout.CacheRail` must delegate stations outside [0, L] to `ExtendRail` instead of silently clamping to endpoints. `ExtendRail` uses endpoint tangent and endpoint face normal for short bounded continuation. Uncertainty outside the owner face still requires independent collision inspection.

## Anti handoff
After testing create `Prototypes/CircleFaceTubeLayout/V3_IMPLEMENTATION_REPORT.md` in this branch, record commit hashes, test commands + exit codes, actual H thresholds/clearances, source copied models, debug log paths, screenshots, any critical fixes, and remaining risks. Ask Codex to independently review before merge.