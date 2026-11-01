# R72 — measured oblique joints and folded-sheet native Cuts

Qualified on 2026-10-31: **six actual API cases, 72 new native Cut-Extrudes and 93 new Fully Defined sketches**. The exact qualified GhostPreview source was copied into the main class for manual rebuild. No main Add-in Build/Deploy, DLL/PDB/project change or merge occurred.

| Case | Member angle | Cuts | New Full3 Sketches | Native gap mm | Result |
|---|---:|---:|---:|---:|---|
| 2222-04-TI-b4-fr5-1 x 2222-04-TI-b4-fr6-1 | 90.000000° | 22 | 33 | 0.100000000 | PASS API |
| 2354-04-lc-B11-fr04-1 x 2354-04-lc-B11-fr03-1 | 81.507115° | 6 | 8 | 0.100000000 | PASS API |
| 4458-04-TI-b2-fr4-1 x 4458-04-TI-b1-fr3-3 | 90.000000° | 10 | 12 | 0.100000000 | PASS API |
| 4458-04-TI-b2-fr4-1 x 4458-04-TI-b2-fr5-1 | 90.000000° | 13 | 15 | 0.100000000 | PASS API |
| 4458-04-TI-b2-fr1-1 x 4458-04-TI-b1-fr2-2 | 90.000000° | 9 | 11 | 0.100000000 | PASS API |
| 4458-04-TI-b2-fr1-1 x 4458-04-TI-b2-fr6-1 | 90.000000° | 12 | 14 | 0.100000000 | PASS API |

All 72 Cuts read back as **Blind, NormalCut=true, API thickness + SheetCutOverrun 0.10 mm**. Every Cut's native/Preview B-rep symmetric difference is zero. Final pair gaps are approximately **AssemblyFitGap 0.10 mm**, with zero interference. All 93 created sketches retain existing original-owner Face2 support, native constrained status 3, zero dangling/overdefining/FIX/Convert relations, and zero new reference planes. Body Check2/Check3 and final cumulative checks passed.

The 2222 input already contains nine AFJ Cuts; these preserved input features are not part of the 72 new events. Earlier authored 43-Cut / 42-before-after inventories and the historical 93 authored Cuts are separate data.

## Implemented behavior

The solver uses actual nonparallel member axes and measured near/far boundaries instead of requiring 90°. Duplicate family authorities need full-interval physical equivalence. Secondary crossarm material needs original paired-flat and retained-cover B-rep witnesses. Inner/outer Bend radii govern physical clearance. Opposite Bend entry skins receive separate Blind Normal slices with a geometrically constrained original-profile midpoint guide. A pre-cut oblique end uses four native Vertex coordinate relations for an axial construction cap while preserving the exact Preview contour.

The existing A/B selection, command/session chain and cleanup were preserved. Shorter-frame covering priority, independent Cut Plans and strict native/Preview validation remain mandatory. Unknown geometry still returns UNVERIFIED_RULE. See [rules and bounded evidence](RULES_REVIEW.md).

## Reproducible evidence

- [Six-case matrix](TEST_MATRIX.csv), [72 new Cut parameter/body observations](NATIVE_CUT_RESULTS.csv), [93 final sketch graphs](SKETCH_CONSTRAINT_RESULTS.csv).
- [Structured audit](API_EVIDENCE.json) retains final runs 26/27/29 and distinct earlier failures. [Observation history](CUT_OBSERVATION_HISTORY.csv) includes retries with explicit roles; its rows must not be summed as distinct final Cuts.
- [Source/DLL handoff](SOURCE_INTEGRATION.json) and [manual rebuild / limits](MANUAL_BUILD_AND_LIMITS.md).

Both current reported cases use exact copied current input without stock, suppression or Assembly-transform preparation. The four-corner 4458 regression uses the same explicitly reviewed bounded-copy stock preparation as R71: reviewed Cut/dependent suppression and BaseFlange D1 readback, including both ends of shared members. Source/snapshot/copy disk hashes stayed unchanged; copies closed NoSave. No original CAD, CAD images, raw geometry, private paths, persistent-reference tokens or binaries are published.

The authored completed-reference comparison records **DIFFERENCES** separately from native/Preview equality. Run 28 remains an overall FAIL because the runner mistakenly required exact authored-reference reproduction; its four successful pair-level applies are retry observations. Run 29 preserves every native/Preview, KEEP, parameter, graph, gap, Body and history gate, with the intended optional reference audit. No failed run was relabeled.

This is **API harness qualification of the actual candidate classes**, not a rebuilt installed Add-in/PMP test. Visible/smooth Preview, production speed, standalone Part reopen, general failure rollback, unmeasured exposed-edge coverage and arbitrary geometries remain UNVERIFIED.
