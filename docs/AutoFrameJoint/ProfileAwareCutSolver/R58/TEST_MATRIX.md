# R58 test matrix

The installed four-corner loop passed under CAD-first load order on the same in-memory disposable copies. The earlier crash cause remains unresolved.

| Case | Execution scope | Result | New Cut verification | Geometry / display |
|---|---|---|---:|---|
| R57 installed user run | User-built DLL; native PropertyManager close | `FAIL_NATIVE_HEAP_CORRUPTION` | 0 completed; one provisional factory Feature | Final body and rollback unverified |
| R58 VSTA identity probe | Separate `testconsole.exe` process | `REJECTED_PROCESS_IDENTITY` before modeling | 0 | Not attempted |
| R58 initial diagnostic identity | Owned SOLIDWORKS; main Add-in absent | `REJECTED_MISSING_MAIN_ADDIN` | 0 | Not attempted |
| R58 run 07 deployed-DLL identity | Actual `SLDWORKS.exe`, DefaultDomain, native main STA | `API_VERIFIED` / `PASS_IDENTITY_ONLY` | 0 | Identity only |
| R58 run 07 full-request entry | Six copied documents and baseline read | `REJECTED_DIAGNOSTIC_RPC_OWNER_STA` before AFJ command | 0 | Not attempted |
| R58 run 08 deployed-DLL identity | Same installed host and exact DLL | `API_VERIFIED` / `PASS_IDENTITY_ONLY` | 0 | Identity only |
| R58 run 08 copied-Part opening | Subsequent fixture cycle; two Parts opened | `FAIL_NATIVE_HEAP_CORRUPTION_DURING_THIRD_OPEN` before AFJ command | 0 | Not attempted; parent audit interrupted |
| R58 run 09 fresh-host opening | Exact DLL identity passed; first Part opened | `FAIL_API_SERVERFAULT_DURING_SECOND_OPEN`, `0x80010105`; process alive | 0 | Not attempted; no matching native-crash event |
| R58 run 10 installed first pair | Open copies before loading deployed main DLL; actual native `Close(true)` | `PASS_INSTALLED_NATIVE_AFTERCLOSE` | 4 actual Cuts | Visible Preview unverified |
| R58 run 10 independent final B-rep | Parent API after installed bridge success | `GEOMETRY_VERIFIED` for bounded final checks | Same 4 Cuts; 4 full-status-3 original-Face Sketches; 0 planes | Healthy solids; 0.10 mm gap; zero overlap |
| R58 remaining three corners | Actual installed flow on the same unsaved copies and loaded DLL | `PASS_INSTALLED_NATIVE_AFTERCLOSE` for each | 12 additional Cuts and 12 full-status-3 Face Sketches; 0 planes | Final loop total 16 Cuts / 16 Sketches |
| R58 final four-corner rechecks | Independent parent API after the last corner | `GEOMETRY_VERIFIED` for bounded final checks | All 16 Sketches rechecked; all four pair gaps rechecked | All gaps about 0.10 mm; overlap 0; code 0 |
| R58 private lifecycle candidate | Detached six-source compilation and managed tests | `COMPILE_AND_MANAGED_TESTS_ONLY` | 0 native tests | Not a native crash fix |

Run 08 has a matching Windows native-crash event and an independent post-crash disk-hash audit. Its incomplete parent status `RUNNING` is not a result. Six source files and six copies remained byte-identical; no body or rollback claim is made after death.

Run 10 met the first-pair criteria: four actual Cut Features, four fully constrained 2D Sketches supported by original `Face2` references, zero new reference planes, healthy final solids, minimum gap `0.09999999999976694 mm`, and zero positive overlap. The parent intersection return code was 0. All source, copy and protected binary hashes matched.

This native geometry qualification is limited to final body health, gap, intersection and Feature/Sketch properties. It does not establish authored manufacturing equivalence, flat-pattern correctness, startup crash freedom, or visual smoothness.

The final four-corner continuation now establishes the actual installed API loop for all four ordered pairs on these copies. See [FOUR_CORNERS_API.json](FOUR_CORNERS_API.json) and [FOUR_CORNERS_API.csv](FOUR_CORNERS_API.csv). It does not qualify normal main-first startup or broad geometry families. No installed Preview visibility or smoothness PASS is recorded without that observation.

The cumulative 16 generated Cuts are not added to the historical reference inventory of 43 authored Cuts and 42 reference before/after cases.

Matrix rows include repeated validation of the same native Cuts and must not be summed. The authoritative generated total is 16; the four disjoint corner rows in `FOUR_CORNERS_API.csv` each contain four Cuts.
