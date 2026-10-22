# AFJ post-load audit: legacy U Sketch requirements

**Source PR #45 / commit `90876693d34fbaf6b13f0570b548ba7f6b946547`; audit date 2026-10-22.** This report supplements the earlier Preview/solver results. It does not overwrite their historical source/DLL snapshots.

## Findings

1. **The new DLL was loaded.** The user log and disk DLL both report MVID `420dfcfc-a6ee-4861-ae82-f54d916e32e4`. DLL/PDB GUID+age match. All 459 AFJ methods across 94 types match the independently compiled actual-local-source harness under the recorded IL normalization. The source did not drift. Native PDB source checksum proof was unavailable; that is not a checksum mismatch.
2. **The actual run used the legacy U writer.** A=2354 fr03, B=fr04 selected `ORTHOGONAL_U_TALLER_COVER`. It creates `FIXED_PLANE_PRISM` snapshots. The face-attached folded writer only handles operations tagged `FOLDED_RULE:` and was not used in that run.
3. **The Sketch requirement failed in the native copy audit.** Four actual new planes and four underconstrained Sketches were observed. Every Sketch returned status2, zero relations, zero displayed dimensions, reference type4/RefPlane and only its new plane as parent. This is API evidence, distinct from the old writer DTO whose unavailable counters correctly use -1.
4. **Immediate geometry passed; the reported hang remains unexplained.** Four native Cuts matched the reviewed B-rep. Both Part activations and explicit copy-only `EditRebuild3` calls returned true; every recorded post-apply geometry checkpoint remained at zero symmetric difference with no native feature errors. The basic harness completed without reproducing a hang. That result does not establish that the installed interaction is safe in all contexts.

The user log reached AFJ cleanup/AfterClose completion after four matching Cuts (writer5627ms, AfterClose5646ms), then exited with `0xffffffff` later. It contains no timed Part-open span or causal native stack; the parent audit found no matching Windows fault event for this query. None of this identifies the cause of the user-reported Part-open hang.

## Status matrix

| Scope | Result | Evidence limit |
| --- | --- | --- |
| Actual loaded binary identity | VERIFIED | User runtime MVID matches disk; metadata and IL audit |
| Four U native Cut bodies | GEOMETRY_VERIFIED | Exact tested pose and recorded checkpoints |
| U Sketch modeling intent | **REQUIREMENTS_FAIL** | Four new planes, four status2 Sketches, zero relations |
| Copy Part activation and explicit rebuild | COMPLETED | No hang in basic native harness; no installed UI claim |
| Fresh-copy Save / Read-Only reopen | COMPLETED_WITH_SKETCH_REQUIREMENTS_FAIL | Both copies reopened Read-Only with valid bodies, no new feature errors; all four Sketches still status2/zero relations |
| Cause of installed Part-open hang/exit | UNVERIFIED | No causal native stack or matching Part-open event span |
| Proposed U writer rewrite | INFERRED | Design only; not compiled or native-tested |

## Files

- [DLL_VERIFICATION.json](DLL_VERIFICATION.json): disk/runtime identity, IL and PDB evidence.
- [SKETCH_FINDINGS.csv](SKETCH_FINDINGS.csv): four directly measured native Sketches.
- [NATIVE_AUDIT.json](NATIVE_AUDIT.json): basic activation/rebuild audit and separate round-trip status.
- [USER_LOG_EVENTS.csv](USER_LOG_EVENTS.csv): selected sanitized runtime evidence with original line numbers.
- [U_REWRITE_PLAN.md](U_REWRITE_PLAN.md): proposed geometric rewrite preserving the U miter and exact target bodies.

The proposed rewrite must preserve the actual U seam, operation order and KEEP/CUT result. It must not replace the U miter with a different butt solely to avoid Sketch constraints. The plan uses existing Web faces, direct geometric relations and minimal manufacturing parameters, with explicit dependency/rollback/reopen qualification gates.

No original CAD, serialized B-rep, full machine log, DLL/PDB, private path or persistence token payload is published. Main Add-in source, output/deployment settings and historical source-verification reports were not changed by this audit. The round-trip harness saved only its two fresh disposable Part copies, then reopened them Read-Only. Source hashes stayed unchanged; copy hashes changed as expected. This does not extend to original CAD.

## Reopen result and current repair boundary

Both Part copies reopened with OpenDoc errors/warnings 0/0 and Body.Check2=0. Volume changed only by floating-point roundoff on A (2.17e-19 m³), and area remained unchanged. These are body health/volume/area checks after reopen, **not a full symmetric-difference replay after disk reopen**. Before Save, all exact B-rep comparisons were zero. No standalone hang was reproduced. The installed user-reported hang remains UNVERIFIED, and the user cannot currently provide a main-thread Call Stack.

A separate copied-CAD experiment preserved all four U Cut bodies with exact B-rep equality, using the actual outer Web Face and native start offset, with zero new planes. Sketches gained 7/4/9/4 direct own-Frame relations and zero dimensions, but all remained underconstrained. It is not enabled in production. Removing the datum plane alone does not finish the repair: critical seam and cross-Sketch references must also be qualified; underconstrained output must not be described as a completed geometric writer.

Source production files remain unchanged in this post-load audit. The audit contradicts any interpretation that all legacy U Cut Sketches already satisfy the newer modeling requirements.

[U_WEB_FACE_PROBE.json](U_WEB_FACE_PROBE.json) records this representation gate. Critical seam and cross-Frame constraints remain a separate qualification; no full U modeling repair is claimed.

## Direct seam relation qualification

The separate four-point probe passed: all four critical U seam/Wall points were fully constrained by eight direct Coincident relations to actual native Frame geometry, with zero dimensions and zero new planes. This was a Sketch-only gate; no Cut, parameter-follow or installed Add-in result is implied. Finite trimmed Edge checks and Face underlying-plane limits are retained in [U_CRITICAL_POINTS_PROBE.md](U_CRITICAL_POINTS_PROBE.md), [JSON](U_CRITICAL_POINTS_PROBE.json) and [CSV](U_CRITICAL_POINTS_PROBE.csv).

The full eight-vertex cover Sketch is a separate experiment. [U_COVER_MASTER_TRIALS.json](U_COVER_MASTER_TRIALS.json) records every completed trial, including failed native relation creation. The candidate uses direct Frame datums, geometric relations and one shared ToolClosureMargin parameter; no per-point coordinate dimensions, Convert or FIX. These research helpers are not routed into the production Add-in. Full-profile, native Cut, later-operation dependencies, rollback, follow and reopen must be qualified separately before enabling a replacement U writer.
