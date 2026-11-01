# Four linked U Cuts and copy round-trip

Experimental native harness only. The existing Add-in route is not enabled by this report.

Report state: `NATIVE_RUNS_COMPLETED_RAW_STATUSES_RETAINED`.

## Completed records

- `native_u_four_cut_roundtrip_01`: **`FAIL_NATIVE_COPY_POSTLOAD_AUDIT`**, actual Cuts=0, graph checks=0.
  KeyNotFoundException while obtaining graph_checks; not a CAD failure. Raw FAIL retained.
- `native_u_four_cut_roundtrip_02`: **`PASS_NATIVE_FOUR_LINKED_U_CUTS_REBUILD_COPY_REOPEN_HARNESS_ONLY`**, actual Cuts=4, graph checks=32.

## Unfinalized records

None.

## Evidence boundaries

Before Save/close, exact B-rep comparisons apply only to their recorded operation or final checkpoints. After Read-Only reopen, this harness checks body health/type/counts/volume/area and native Sketch/reference state; it does not measure full exact post-reopen B-rep equality.

Only explicitly contained fresh copied Parts/Assembly may Save. Source hashes and expected copy hash changes are reported separately. Do not treat an unchanged-copy flag as required after an explicit copy Save.

Run01's KeyNotFoundException occurred in the harness graph_checks lookup before any native Cut. It is retained as a harness failure, not proof of CAD damage.

Run02 contains five plan-operation records but only **four actual native Cut features**. Operation 3 (NEAR_BEND_CLEARANCE) removed no temporary material and was skipped without creating a feature. The retained raw phase labels AFTER_CUT_4 and AFTER_CUT_5 use the plan-operation index, not the count of created Cuts. Each successful Cut record carries a separate actual_native_cut_sequence.

All four Sketches were fully constrained (status 3) at each of the eight recorded graph checkpoints, including after each actual Cut and the Part/Assembly rebuilds: 80 relations, two dimensions, zero dangling/overdefining relations and actual Face attachment. After Read-Only reopen the four reacquired Sketches retained these totals, and no new reference plane was present.

Production integration, installed UI, dependency/parameter variants and global owned-feature rollback remain separate qualification gates. Future rollback cases must be published separately.

## Artifacts

- [U_FOUR_CUT_ROUNDTRIP.json](U_FOUR_CUT_ROUNDTRIP.json): safe selected data.
- [U_FOUR_CUT_ROUNDTRIP.csv](U_FOUR_CUT_ROUNDTRIP.csv): per-run completion and result matrix.
- [U_FOUR_CUT_ROUNDTRIP_GRAPH.csv](U_FOUR_CUT_ROUNDTRIP_GRAPH.csv): native Sketch graph readbacks.
- [U_FOUR_CUT_ROUNDTRIP_SOURCE_MANIFEST.csv](U_FOUR_CUT_ROUNDTRIP_SOURCE_MANIFEST.csv): frozen run02 source hashes.

The generator requires completed_utc, verified blank cleanup and final hash flags before publishing native results. It never invokes SOLIDWORKS. Frozen actual run02 helper/caller sources are included as .cs.txt, not binaries or an Add-in replacement. No raw native coordinates, transforms, private paths or persistence payloads are included.
