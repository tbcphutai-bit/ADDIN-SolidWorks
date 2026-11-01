# R55 — four corners on one 2354 Assembly copy

Status: **PASS_FOUR_CORNERS_SAME_ASSEMBLY_NATIVE_API_HARNESS_ONLY_NOT_INSTALLED_ADDIN**. Completed: 2026-10-24T05:07:50.2995385Z.

The native SOLIDWORKS API harness completed all four corners sequentially on one disposable Assembly copy. It created 16 native Cut-Extrude Features and 16 fully constrained 2D Sketches on existing Part Face2 supports. New reference planes: **0**. Final independent Body checks report zero target difference and interference, with a 0.10 mm minimum pair gap at each corner.

All four native corner operations in this authoritative run started from the same fresh Assembly copy under the bound source revision.

This is **native API harness evidence**. The installed Add-in UI has not been qualified by that harness. Source-only delivery requires the user's manual Rebuild. Display3 returned success, while the native audit records actual Preview visibility as `UNVERIFIED`; API return codes do not establish that the user saw the bodies. Separate private-image inspection status: `FOUR_STATIC_NATIVE08_API_PREVIEW_CAPTURES_VISUALLY_REVIEWED_NOT_INSTALLED_UI`. Its per-corner observations, when supplied, are preserved separately in TEST_MATRIX.json.

| Corner | Pair | Cover | New Cuts | Gap mm | Overlap mm³ | First Preview ms | Native Apply ms |
|---|---|---|---:|---:|---:|---:|---:|
| 1 | fr01 × fr04 | fr04 | 4 | 0.1 | 0 | 49422 | 488352 |
| 2 | fr04 × fr03 | fr04 | 4 | 0.1 | 0 | 26257 | 408316 |
| 3 | fr03 × fr02 | fr02 | 4 | 0.1 | 0 | 50992 | 450192 |
| 4 | fr02 × fr01 | fr02 | 4 | 0.1 | 0 | 56293 | 471956 |

Cross-process native harness timing: cold Solve 24317–54706 ms; first Preview 26257–56293 ms; unchanged P1 repeat 147–279 ms; warm P1→P2 2699–3934 ms; warm P2→P1 317–525 ms. Cold/new-pair analysis remains slow. Warm cache measurements do not prove fast first Solve, installed Add-in responsiveness or freedom from freezing.

## Evidence meanings

- `API_VERIFIED`: Feature definition, Sketch support/relations, native flags and measured metadata read from SOLIDWORKS API.
- `GEOMETRY_VERIFIED`: native-versus-reviewed-target B-rep, Body checks, gap, interference and the stated local exposed-cap probes.
- `INFERRED`: extension of a manufacturing convention beyond the explicitly qualified orthogonal U-profile family.

## Scope and files

`TEST_MATRIX` records each corner and performance; `NATIVE_CUT_INVENTORY` records actual generated Feature parameters; `SKETCH_CONSTRAINTS` records final constraints. `GEOMETRIC_RULES.md` explains KEEP/CUT allocation. [FLOW.md](FLOW.md) maps the existing Preview and after-close native Apply paths. `SOURCE_DLL_VERIFICATION.md` and `SOURCE_MANIFEST.json` bind evidence to source. `RELATION_CONTEXT_DIAGNOSTIC.json` and optional `LIVE_DATUM_CONTEXT_DIAGNOSTIC.json` preserve native context comparisons; `PAIRED_CACHE_CONTEXT_FIX.json` records the versioned observed native fix. `FEATURE_TREE_CYCLE.json`, `SANITIZED_FAILURES.json`, `MANAGED_UI_SUMMARY.json`, `W23_PROVENANCE.json` and `KNOWN_LIMITS.md` preserve scope and remaining limits.

Source CAD remained byte-identical; copies were closed without document Save/Rebuild/Pack and Go. Existing fr04 Plane 802, Sketch 803 and Cut 809 belong to the protected baseline; R55 creates no new Plane. No original CAD, B-rep payload, viewport image, complete placement coordinates or machine paths are included.

Reports are append-only evidence. A successful later attempt does not convert earlier failed attempts into PASS.
