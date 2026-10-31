# Auto Frame Joint R60 — candidate native PMP/idle API result

PASS_CANDIDATE_NATIVE_PMP_IDLE_1_PAIR

NOT_APPLICABLE_SEPARATE_FIXTURE

Candidate MVID: `8bc29815-5c40-40df-bc74-70551fda957c`.
Candidate source SHA256 supplied for this run: `EF7197D1C7188ECD308076A026B3A9DA17463B732DEA380E44FCB3BDDEF00F46`.

Independently qualified pairs: 1/1; independently observed native Cuts: 8; OptimizeGeometry=false API readbacks: 8.
Final cumulative Sketch checks: 12/12; verified: True.

| Corner | Pair | Qualified | Cuts | Prior/current Sketch graphs |
|---|---|---|---:|---|
| 1 | 4332-04-TI-P-1-fr5-3 x 4332-04-TI-P-1-fr3a-4 | True | 8 | True |

Native Cut readbacks must be Blind, NormalCut=true, and actual API thickness +0.10 mm. OptimizeGeometry=false is read back from every applied Cut. SheetCutOverrun=0.10 mm and AssemblyFitGap=0.10 mm are distinct conventions.

Fixture qualification additionally requires every-corner/final exact Sketch identity and graph checks, final pair gap/overlap, Body B-rep target/health, original feature history, no new planes, and file/session guards.

Actual API thickness/depth distribution:

- t=2.0 mm; Blind=2.1 mm: 8 Cuts.

Native PMP callback/idle qualification: True
Exact installed crash DLL background TaskPane/event subscribers loaded: True
Native Close API returned before Apply; genuine SOLIDWORKS idle notification preceded the posted Apply. No fake idle or nested message pump.

- The candidate actual native PropertyManager selection/AfterClose/idle path was exercised; installed R60 AFJ CommandEntry/SessionManager and rebuilt DLL were not tested.
- The user crash DLL was loaded only for background TaskPane/event subscribers. Its own AFJ command was not invoked.
- The real Ghost Preview ticket was validated before the diagnostic native page; no Preview-button callback qualification is claimed.
- Display3 API status does not prove that the user can see the Preview.
- Failure rollback remains UNVERIFIED in historical failure cases; a successful apply run does not qualify rollback.
- Missing, running, or failed fields remain NOT_AVAILABLE or UNVERIFIED; no source/CAD result is invented.
- The native PMP diagnostic runner records R60 native callback scope and exact source hashes.
- Raw paths, native persistent tokens, full exception stacks and CAD files are intentionally excluded.
