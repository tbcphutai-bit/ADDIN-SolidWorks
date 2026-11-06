# R71 exact-run public review

**PASS_FOUR_CORNERS_API_HARNESS_ONLY**

This report evaluates one exact run against the four required 4458 corner pairs in one cumulative disposable assembly. Captured scope is stated below; an incomplete run remains FAIL/UNVERIFIED. All claims are scoped to captured API and B-rep evidence.

| Corner | Pair | Native Cuts passed / captured / planned | Preview gap mm | Native gap mm | Result |
|---|---|---:|---:|---:|---|
| C1 | 4458-04-TI-b2-fr4-1 x 4458-04-TI-b1-fr3-3 | 10 / 10 / 10 | 0.09999999999976694 | 0.09999999999250023 | PASS |
| C2 | 4458-04-TI-b2-fr4-1 x 4458-04-TI-b2-fr5-1 | 13 / 13 / 13 | 0.09999999999887876 | 0.09999999999932285 | PASS |
| C3 | 4458-04-TI-b2-fr1-1 x 4458-04-TI-b1-fr2-2 | 9 / 9 / 9 | 0.09999999999987796 | 0.09999999999987796 | PASS |
| C4 | 4458-04-TI-b2-fr1-1 x 4458-04-TI-b2-fr6-1 | 12 / 12 / 12 | 0.09999999999987796 | 0.09999999999904581 | PASS |

Captured native Cuts: 44; independently passing Cut rows: 44. Final cumulative created sketches checked: 52.

Measured wall duration: 265.52489 seconds. Per-corner Preview and native-apply timings appear in TEST_MATRIX.csv; Preview totals include every recorded call.

Qualification is fail-closed: exact top-level four-pair PASS, four pair PASS/native-apply results, all Cut/Preview parity, target symmetric differences exactly zero, every native NormalCut/Blind t+0.10 mm readback, preserved Face2 Full3 sketch graphs, and final cumulative pair/target/sketch checks must pass. Candidate binary, compile inputs, and parent run hashes must bind to this exact run.

## Rules represented by this candidate

- Per-sheet finite measured contacts define the eligible cut regions.
- Every native Cut uses Blind depth t + 0.10 mm and NormalCut=true; AssemblyFitGap is a separate in-plane fit parameter.
- Terminal entry-face choice uses actual R or R+t as measured for the selected entry face.
- Bend removal uses exact eligible inner/outer-radius (Ri/Ro) radial windows plus measured adjacent-flat carryover.
- The approximately 2.05 degree source endpoint uses a projection helper for the measured finite endpoint datum.
- Sketches remain supported by a persistent actual existing Face2. No new reference planes, Convert Entities, or Fixed relations are permitted.
- Native and Preview B-reps must have symmetric difference <= 1e-12 m3; created sketches must read Full3 (status 3) after every corner and at the cumulative final check.

## Prepared stock and source evidence

Prepared stock is an explicit test-fixture operation: reviewed native Cut-ID suppression plus bounded BaseFlange D1 edits on copies, including both ends of fr1 and fr4. SUMMARY.json records the IDs, bounded readbacks, source CAD SHA256 digests, candidate compile source digests, and private audit digests without private paths.

Source/snapshot bytes unchanged: True; disposable-copy disk bytes unchanged: True; original CAD byte proof complete and unchanged: True.

## Optional completed-reference comparison

Recorded result: **DIFFERENCES**. This comparison is optional and separate from finite measured-contact qualification. A DIFFERENT row is not a PASS against the completed reference; no exact reproduction of old completed cuts is claimed.

The historical read-only inventory captured 93 native Cuts across 7 detailed parts; all NormalCut=true: True. Historical counts are separate from candidate results.

## Limitations

- Evidence is from the API harness only. Installed Add-in behavior, selection/PMP callbacks, user UI behavior, and actual rendered Preview visibility are UNVERIFIED.
- This report qualifies only the exact four corner pairs, prepared stock, candidate hashes, and captured run stated here; it does not establish a solution for all geometries.
- The terminal bend projection rule is UNVERIFIED for turns greater than 90 degrees.
- Exposed-edge coverage for angled profiles remains UNVERIFIED unless explicitly measured. A successful native Cut/Preview parity check does not establish that every exposed edge is covered.
- Failure-path rollback is UNVERIFIED unless a captured rollback check explicitly verifies it; this report does not establish a general rollback fix.
- Prepared stock uses explicit reviewed native Cut-ID suppression and bounded BaseFlange D1 edits on disposable copies. fr1 and fr4 require both ends. Suppression can affect dependent holes/patterns; unchanged production hole preservation is not claimed.
- The completed original reference is an optional comparison. DIFFERENCES are reported explicitly and are not a PASS against that reference.
- No original CAD, private machine paths, usernames, raw profile coordinates, persistent reference tokens, or body binaries are included in these generated files.
- Elapsed times are captured API timings and wall-clock duration; no speedup or UI responsiveness is inferred.

SUMMARY.json contains exact-run provenance, prepared-stock details, gates, and optional reference differences. TEST_MATRIX and NATIVE_CUT_RESULTS provide CSV/JSON summaries. SKETCH_CONSTRAINT_RESULTS and TARGET_PARITY_RESULTS preserve sanitized cumulative checks. This generator performs no CAD mutation, publishing, source copy, or Git operation.

## Source handoff

The exact qualified GhostPreview source has been copied into the main class for the user to rebuild. [Source integration proof](SOURCE_INTEGRATION.json), [algorithm and changed methods](ALGORITHM_AND_SOURCE.md), [manual rebuild and limitations](MANUAL_BUILD_AND_LIMITS.md), and [earlier failure history](RESEARCH_FAILURE_HISTORY.csv) are included. No main Add-in build/deployment occurred.
