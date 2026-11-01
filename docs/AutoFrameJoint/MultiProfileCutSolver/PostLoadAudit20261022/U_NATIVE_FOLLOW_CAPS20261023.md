# U Sketch caps and native width-follow milestones

**2026-10-23 — experimental native copy harnesses.** These observations supplement the previous U milestones. They retain the actual `ORTHOGONAL_U_TALLER_COVER` for A=2354 fr03 and B=fr04. They create no native Cut, call no Save and do not integrate a production writer or test installed UI behavior.

## Four fully constrained Sketches, no new planes

`native_u_two_caps_01` returned **`PASS_NATIVE_TWO_CAP_SKETCHES_ZERO_DIM_ZERO_PLANE_NO_CUT_NO_FOLLOW_TEST`** on SOLIDWORKS32.5.0.

| Native Sketch | Constrained status | Relations | Driving dimensions | New planes |
| --- | ---: | ---: | ---: | ---: |
| Short cover/master | 3 | 28 | 1 ToolClosureMargin | 0 |
| Long losing profile | 3 | 36 | 1 AssemblyFitGap | 0 |
| Short `U_RULE:NORMAL_BEND_CAP` | 3 | 6 | 0 | 0 |
| Long `U_RULE:FAR_BEND_CLEARANCE` | 3 | 10 | 0 | 0 |
| Total | All four fully constrained | 80 | 2 | 0 |

All four had zero dangling/overdefining relations. The caps were closed and attached to actual existing Web Faces (reference type2), with correct Part ownership and unchanged rectangle seeds. Six actual source relation definition identities were verified across the two caps. Both cap dimensions come from their actual source master/loser points and shared gap geometry; no copied local coordinate/gap dimension or blanket FIX was added.

The short cap reacquired4 lines; the long cap reacquired5 lines, including its construction guide, from the original Assembly-context Sketch. The existing master margin is13 mm; the single losing gap dimension is0.10 mm. `NORMAL_BEND_CAP` is an operation name here, not evidence that a native Cut had NormalCut enabled. No Cut was created; cap native B-rep equivalence, follow, rebuild/reopen and failure rollback remain untested in this run.

## Width V3: actual source geometry and both endpoint sets follow

`native_u_width_follow_03` returned **`PASS_COPY_NATIVE_WIDTH_FOLLOW_SKETCHES_ONLY_NO_CUT_NO_SOLVER_RERUN`**. It tested the master and losing Sketches; the caps were not created in this width test.

The exact original base-profile driving width dimension was changed on a disposable copy. Both dimension readback and the actual native Body width measured70→71→70 mm. `SetSystemValue3` change/restore status was0. The probe rebuilt copied native geometry and measured its existing Sketches without rerunning the AFJ solver or creating replacement profile entities.

| Gauge | Source dimension / native Body width | Normal line gap | Signed line gap | Master/loser status | Feature errors |
| --- | --- | --- | --- | --- | --- |
| Before | 70 / 70 mm | 0.1000000000002084 mm | −0.1000000000002084 mm | 3 / 3 | 0 / 0 |
| Width +1 mm | 71 / 71 mm | 0.1000000000003135 mm | −0.1000000000003135 mm | 3 / 3 | 0 / 0 |
| Restored | 70 / 70 mm | 0.09999999999981588 mm | −0.09999999999981588 mm | 3 / 3 | 0 / 0 |

Parallel cosines stayed within numerical roundoff of1. Both Sketches had zero dangling/overdefining relations at all three gauges. The signed gap retained its negative side; this is more specific than an unsigned distance alone.

Symmetric nearest-endpoint set distance, derived from the recorded native endpoints, measured:

- Master endpoint-set movement: **1.000000000000334 mm**.
- Losing endpoint-set movement: **0.9995520348344833 mm**.
- Restored master endpoint-set error:3.510833468576701e−16 m.
- Restored losing endpoint-set error:3.122502256758253e−17 m.
- Restored component transform maximum difference:0.

The sampled master far endpoint itself remained stationary. V3 qualified the whole endpoint sets; it does not claim every endpoint translated1 mm. The losing movement also need not equal1 mm because its geometry is constrained by the changed seam and fixed native datums.

These are native Sketch-follow and source-width observations. The0.10 mm value is the measured **Sketch-line normal gap**, not a post-Cut material clearance, coverage or complete-joint fit measurement.

## Retained V1/V2 failures

`native_u_width_follow_01` and `native_u_width_follow_02` remain **`FAIL_OR_UNVERIFIED`**. Their assertions observed one master far endpoint; a stationary far endpoint did not establish whether the whole seam had failed to follow. Their finally path verified source-parameter restoration, not both endpoint sets. The later V3 pass does not rewrite those raw results.

[U_WIDTH_FOLLOW_V1_V2_AUDIT_HISTORY.md](U_WIDTH_FOLLOW_V1_V2_AUDIT_HISTORY.md) preserves that historical audit with chronology stated. Its pending-V3 statements describe the time before V3 completed.

## Boundaries and protection

Both new completed runs preserved all original and copied file hashes and restored a blank owned session. No Cut or Save was called. The width experiment establishes one70→71→70 mm source-width change for these two native Sketches. It does not qualify cap follow, arbitrary widths/return/clamp cases, dependency cycles, complete four-Cut transactions, later topology changes, rollback or installed Add-in responsiveness. The parent is testing native four-Cut construction separately; no result for that work is claimed here.

Raw native coordinates, transforms, source-dimension full machine names, private paths, persistence payloads and original CAD are omitted. Computed displacement, width, gap, native status and identity counters are retained.

## Artifacts and source provenance

- [U_NATIVE_FOLLOW_CAPS20261023.json](U_NATIVE_FOLLOW_CAPS20261023.json): selected native observations with retained historical failures.
- [U_NATIVE_FOLLOW_CAPS20261023.csv](U_NATIVE_FOLLOW_CAPS20261023.csv): two completed bounded experiments and two historical failure rows.
- [U_NATIVE_FOLLOW_CAPS20261023_GAUGES.csv](U_NATIVE_FOLLOW_CAPS20261023_GAUGES.csv): three native width/line-gap gauges.
- [U_NATIVE_FOLLOW_CAPS20261023_SKETCHES.csv](U_NATIVE_FOLLOW_CAPS20261023_SKETCHES.csv): four Sketch counts.
- [U_NATIVE_FOLLOW_CAPS20261023_SOURCE_MANIFEST.csv](U_NATIVE_FOLLOW_CAPS20261023_SOURCE_MANIFEST.csv): SHA256 of actual tested helper/caller source copies.

Latest actual `.cs` copies are published as `.cs.txt` under `evidence/`: `AfjUTwoCapSketchProbeV2`, `UWidthFollowProbeV3`, `UTwoCapsHarness`, and `UWidthFollowV3Harness`. The caps helper uses the qualified Assembly-context entity reacquisition. The copies are native research helpers requiring existing internal AFJ/harness dependencies, not a production source replacement. Their actual-source hashes differ from older frozen `.cs.txt` candidates where the root adapted the native test source; the manifest identifies the published versions exactly.
