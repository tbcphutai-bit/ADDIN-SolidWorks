# Auto Frame Joint R59 — sanitized API result

PASS_API_HARNESS_4_PAIRS

PASS_API_HARNESS_ALL_FOUR_CORNERS

Candidate MVID: `f12c4f17-6463-4dc8-bf55-39d0dfd0ce00`.
Candidate source SHA256 supplied for this run: `036844470F9C779F1A0392EC0AB25ECF6D3BBAFA2B366645C460DD39064E9525`.

Independently qualified pairs: 4/4; independently observed native Cuts: 33; OptimizeGeometry=false API readbacks: 33.
Final cumulative Sketch checks: 49/49; verified: True.

| Corner | Pair | Qualified | Cuts | Prior/current Sketch graphs |
|---|---|---|---:|---|
| 1 | 2354-04-lc-B11-fr01-1 x 2354-04-lc-B11-fr04-1 | True | 7 | True |
| 2 | 2354-04-lc-B11-fr04-1 x 2354-04-lc-B11-fr03-1 | True | 6 | True |
| 3 | 2354-04-lc-B11-fr03-1 x 2354-04-lc-B11-fr02-1 | True | 10 | True |
| 4 | 2354-04-lc-B11-fr02-1 x 2354-04-lc-B11-fr01-1 | True | 10 | True |

Native Cut readbacks must be Blind, NormalCut=true, and actual API thickness +0.10 mm. OptimizeGeometry=false is read back from every applied Cut. SheetCutOverrun=0.10 mm and AssemblyFitGap=0.10 mm are distinct conventions.

Fixture qualification additionally requires every-corner/final exact Sketch identity and graph checks, final pair gap/overlap, Body B-rep target/health, original feature history, no new planes, and file/session guards.

Actual API thickness/depth distribution:

- t=2.5 mm; Blind=2.6 mm: 33 Cuts.

- Installed DLL and actual PropertyManager callbacks were not qualified by this harness.
- Display3 API status does not prove that the user can see the Preview.
- Failure rollback remains UNVERIFIED in historical failure cases; a successful apply run does not qualify rollback.
- Missing, running, or failed fields remain NOT_AVAILABLE or UNVERIFIED; no source/CAD result is invented.
- Raw paths, native persistent tokens, full exception stacks and CAD files are intentionally excluded.
