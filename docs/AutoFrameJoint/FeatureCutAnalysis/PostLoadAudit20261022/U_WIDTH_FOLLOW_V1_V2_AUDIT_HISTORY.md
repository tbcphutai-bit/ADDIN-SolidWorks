> Historical audit written before V3 completed. V1/V2 raw FAIL_OR_UNVERIFIED remain valid. The later V3 result is reported separately in U_NATIVE_FOLLOW_CAPS20261023.md; pending-V3 statements below describe the audit time.

# Width-follow probe V1/V2 audit

2026-10-23. Read-only review of UWidthFollowProbe.cs, UWidthFollowProbeV2.cs, their harness call sites and native_u_width_follow_01/02 evidence. No native execution, source edits or compilation was performed for this audit.

## Conclusion

**Actual complete seam following remains UNVERIFIED until the two-endpoint V3 measurement completes.** Retain both raw FAIL_OR_UNVERIFIED results. Their specific far-endpoint assertion is insufficient to conclude that the entire native master seam failed to move.

V1 uses the native line's second endpoint, mn[1]. V2 picks a semantic far endpoint by the actual Frame basis. Both then assert movement of only that one point. A line can rotate about a stationary far endpoint while its other endpoint follows a changed source datum. Changing the endpoint label alone does not resolve that observation gap.

That possible anchored-far/changed-near behavior is an INFERRED explanation, not a measured result from V1/V2. The records do not contain the other master endpoint.

## Direct observations in both native runs

| Observation | Before | Width +1 mm | Evidence |
|---|---|---|---|
| Original source driving dimension | 70 mm | 71 mm | API_VERIFIED |
| Native solid B-rep span along measured Web-width axis | 70 mm | 71 mm | GEOMETRY_VERIFIED |
| Master constrained status | 3 | 3 | API_VERIFIED |
| Losing Sketch constrained status | 3 | 3 | API_VERIFIED |
| Master/loser Feature error codes | 0 / 0 | 0 / 0 | API_VERIFIED |
| Native master/loser normal line gap | 0.1000000000002084 mm | 0.1000000000003135 mm | API-derived native geometry |
| Absolute parallel cosine | 0.9999999999999999 | 1.0000000000000002 | API-derived native geometry |
| Sampled master far endpoint displacement | — | 0 | API-derived native geometry |
| Sampled losing far endpoint displacement | — | approximately 1.092866883 micrometres | API-derived native geometry |
| Component transforms | Initial arrays | Identical arrays | API_VERIFIED |

The source-change test is more than an assigned-number check: it identifies an actual original driving dimension from the base-flange profile, changes it through SetSystemValue3, rebuilds the copied Part and Assembly, then measures the changed native Body's extreme-point span. This establishes actual source geometry changed in the intended width direction for these runs. Span alone does not identify which absolute width-side datum moved.

Both harnesses call AfjJointSolver.Solve once before creating the master/loser Sketches. The width probe then changes/rebuilds native source geometry without invoking the solver or generating new profile entities. SolverRerun=false is consistent with the inspected code; the boolean alone would not establish that fact.

## Restoration limitation

The probes abort at the single-endpoint assertion before recording the normal RESTORED_ORIGINAL_WIDTH gauge. Their finally path restores the dimension, rebuilds, and sets Restored=true by checking the source dimension value only. Therefore V1/V2 Restored=true must be described as **source parameter restoration observed**, not proof that both seam endpoints, Body span and every reference returned to the initial geometry.

V1's AssemblyContextRestored=false is also not proof of a persisted context failure: its success-path assignment was skipped by the exception. V2 rechecks actual active Assembly/edit-target context after finally and records true. The outer harness reports blank-session cleanup and unchanged source/copy hashes for both runs.

## V3 measurement review

The current V3 source records both native master and losing endpoints, source dimension/span, transforms, gap/parallelism, Feature error codes, status3 and dangling/overdefining counts. It checks the maximum nearest-endpoint displacement to avoid start/end reversal artifacts and requires movement greater than 1e-7 m. This repairs the far-only movement test.

For publication, inspect the complete before/changed/restored gauges:

1. Source dimension and actual B-rep span change to the requested width and return to the original width.
2. At least one actual master endpoint changes. Endpoint-set comparison should tolerate start/end reversal; symmetric nearest-endpoint comparison or explicit endpoint-ID matching gives the clearest restoration check.
3. Both master and losing endpoint sets return to the initial geometry. Current V3 asserts master restoration only; losing endpoints are recorded and can be checked from the raw gauges.
4. Component transforms remain unchanged, so native profile-coordinate movement is not disguised component placement.
5. Native master/loser status/error/dangling/overdefinition and gap/parallel checks pass in all three gauges.
6. The normal-gap value uses an unsigned norm. Preserve the intended side/branch if claiming more than the narrow Sketch-follow behavior. Complete Cut-rule following still requires native material/KEEP/CUT validation.

No V3 native result is claimed by this audit. A positive V3 result can qualify this source-width change and restoration of these two Sketches without rerunning the solver. It cannot alone qualify four Sketches, later Cuts/caps, arbitrary profile changes, dependency-cycle safety, global rollback or installed Add-in responsiveness.
