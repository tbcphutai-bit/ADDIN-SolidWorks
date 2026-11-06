# R61 — terminal overhang material qualification

PASS_R61_TERMINAL_4_PAIRS

Fixture: `2354_CURRENT_USER_PROVIDED`.
Ghost source SHA256: `8C4218B6205309407565109C96B89E328449FABFE39D2567CA8B26D3228BE195`; UI SHA256: `27BE94F7A1D12FB25EB1C90733068259E9326BF6BEAC1340F41C1D0A5F13914E`.

Native Cuts: 35; terminal sheet Cuts: 5; final constrained Sketches: 51.
Positive original terminal flat witnesses: 5; actual-R Bend witnesses: 1.
Independent terminal material qualified: True; final outside flat/Bend checks: True/True.
Actual native PMP/idle scope: NOT_RUN_DIRECT_API.

Each reported overhang must have positive original material, zero residual after Preview and native Cut, and preserved inside KEEP material. All outside masks are rechecked after the final corner. Normal Cut/Blind=t+0.10mm and existing-Face2 constrained Sketches remain independently checked.

| Corner | Pair | Cuts | Flat witnesses | Bend witnesses | Qualified |
|---|---|---:|---:|---:|---|
| 1 | 2354-04-lc-B11-fr01-1 x 2354-04-lc-B11-fr04-1 | 5 | 1 | 0 | True |
| 2 | 2354-04-lc-B11-fr04-1 x 2354-04-lc-B11-fr03-1 | 7 | 1 | 0 | True |
| 3 | 2354-04-lc-B11-fr03-1 x 2354-04-lc-B11-fr02-1 | 11 | 1 | 0 | True |
| 4 | 2354-04-lc-B11-fr02-1 x 2354-04-lc-B11-fr01-1 | 12 | 2 | 1 | True |

- Actual installed AFJ command/Preview button/visible rendering after user rebuild not qualified by these candidate tests.
- Failed native rollback remains UNVERIFIED; a successful apply does not prove rollback.
- Current user-provided 2354 and recovered historical bytes are separate immutable fixtures; previous reports remain unchanged.
- Positive original outside material and zero residual, plus inside KEEP and actual-R masks, are checked separately from native=Preview.
- Raw paths, tokens, CAD, binary and full private exception logs are excluded.
