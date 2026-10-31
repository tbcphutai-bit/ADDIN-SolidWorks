# R61 — terminal overhang material qualification

PASS_R61_TERMINAL_1_PAIRS

Fixture: `4332_ORIGINAL_REVERSED`.
Ghost source SHA256: `CD1D51FE6F7D867028AAE945B6F69BFCE063A425CD0CC13D5A6D79F0C6AC503B`; UI SHA256: `27BE94F7A1D12FB25EB1C90733068259E9326BF6BEAC1340F41C1D0A5F13914E`.

Native Cuts: 12; terminal sheet Cuts: 4; final constrained Sketches: 16.
Positive original terminal flat witnesses: 4; actual-R Bend witnesses: 2.
Independent terminal material qualified: True; final outside flat/Bend checks: True/True.
Actual native PMP/idle scope: NOT_RUN_DIRECT_API.

Each reported overhang must have positive original material, zero residual after Preview and native Cut, and preserved inside KEEP material. All outside masks are rechecked after the final corner. Normal Cut/Blind=t+0.10mm and existing-Face2 constrained Sketches remain independently checked.

| Corner | Pair | Cuts | Flat witnesses | Bend witnesses | Qualified |
|---|---|---:|---:|---:|---|
| 1 | 4332-04-TI-P-1-fr5-3 x 4332-04-TI-P-1-fr3a-4 | 12 | 4 | 2 | True |

- Actual installed AFJ command/Preview button/visible rendering after user rebuild not qualified by these candidate tests.
- Failed native rollback remains UNVERIFIED; a successful apply does not prove rollback.
- Current user-provided 2354 and recovered historical bytes are separate immutable fixtures; previous reports remain unchanged.
- Positive original outside material and zero residual, plus inside KEEP and actual-R masks, are checked separately from native=Preview.
- Raw paths, tokens, CAD, binary and full private exception logs are excluded.
