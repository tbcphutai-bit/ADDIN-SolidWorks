# Auto Frame Joint: folded profiles and native Sketch research

**Source and report snapshot — 2026-10-22.** The source audit is complete for commit `90876693d34fbaf6b13f0570b548ba7f6b946547` on `feat/afj-multi-profile-cut-preview-20261022`: three existing classes were patched against the exact local/branch baselines, while the UI, project, installed DLL and PDB were preserved. See [SOURCE_VERIFICATION.json](SOURCE_VERIFICATION.json). The main DLL has not been rebuilt or deployed. Native copy-harness evidence remains distinct from an installed Add-in interaction.

## Verified in this snapshot

- **2354 fr02 × fr01:** the new folded-profile BUTT solver produced valid temporary bodies with a 0.10 mm minimum gap and zero overlap. The short profile retained all six strips and five bends.
- **Native end-section writer:** two actual Cut-Extrude features matched the reviewed bodies with zero measured symmetric difference. Both Sketches were face-attached and fully constrained by direct geometric relations; counts were 34/16 relations, zero driving dimensions and zero new reference planes.
- **Exposure diagnostic:** the long Cut face was 364.602875 mm²; 29.977776 mm² remained exposed at the defined B-rep probe section. The result explicitly permits and reports this exposure.
- **Separate cross-Frame end-condition experiment:** Up To Surface and Offset From Surface references preserved both bodies. A disposable-copy rebuild and long stock parameter change from 3.20 m to 3.21 m and back retained zero measured body difference. This experiment is not yet integrated into the production candidate at this snapshot.
- **1618 fr3 × fr4 stock-copy geometry:** after existing Cuts were suppressed only on disposable copies, the six-strip × four-strip pair passed temporary geometry, one retained solid per Frame, zero overlap and a 0.10 mm gap. Its long Cut probe reported 127.444101 mm² exposed out of 330.478760 mm². That geometry-only run created no native Cuts. A subsequent stock-copy native writer run created two Cut-Extrude features, both fully constrained with 34/22 direct relations, zero dimensions/planes and zero body difference.
- **3615 T1 stock copies:** four actual Cuts passed with a 0.10 mm gap, zero overlap and zero body difference using the legacy U/fixed-plane writer. This does not meet the new face-attached/direct-relation writer requirement. The T2 shared-Part test passed the expected scope refusal with zero Cuts and no history changes.
- **Reversed 2354 A/B and failure rollback:** two actual Cuts passed with relation counts 16/34, zero body difference and a 0.10 mm gap. An injected failure after both native Cuts restored both Parts’ geometry and Feature Trees. The subsequent stale-ticket case hit a native session fault before solving. A later fresh-session case passed the expected `PREVIEW_STALE` refusal with zero Cuts and unchanged original bodies; the earlier fault remains recorded.
- **Real public SessionManager native path:** P1/P1/P2/P1 took 38260/287/739/429 ms: one public solver call followed by three cache hits. Unchanged P1 skipped Hide/Display3; P2 reused KEEP. Public apply created two matching native Cuts, then manager close cleaned all tracked bodies. Display3 succeeded, but actual visibility remains unverified without installed UI inspection.
- **Final 2354 fr04 × fr03 legacy regression:** the boundary candidate retained its U/Taller-U result: four native Cuts, zero body difference/overlap and a 0.10 mm gap. Its old fixed-plane writer still does not meet the latest Sketch requirement.
- **Earlier installed Add-in log:** fr04 × fr03 completed Preview and native OK, creating four verified Cuts. This confirms that earlier pipeline, not the new folded-profile/Sketch changes.

## Not yet complete

- The new candidate has not been tested through native OK in the installed Add-in.
- The old U-rule native writer still needs the requested face/relationship rewrite.
- The current already-trimmed 1618 fr3 × fr4 specimen refused the cutoff as outside the selected end half. That failure remains separate from the stock-copy pass; the subsequent stock-copy writer pass does not qualify the already-trimmed specimen.
- Cross-end Undo qualification failed: finishing the recording returned false twice and no Undo was attempted. That adapter is not qualified or integrated. Generic face-writer geometry/tree rollback now passed for the reversed 2354 pair. Undo stack/dirty-state restoration, complete two-Part cycles, broad parameter-follow tests and flat-pattern checks remain unverified.
- The first public Preview solve still took 37.369 seconds; repeat requests were subsecond with cache reuse. Smooth installed Preview and initial latency are not qualified.

The earlier direct other-Frame movement probe remained unverified because mates reset the requested movement. A subsequent copy-only probe temporarily suppressed 33 mates, retained the other Frame’s 1 mm translation after rebuild, and verified that the exact native Sketch point followed by 1 mm while the owning Frame stayed fixed. All 35 original mate states, fixed state, transforms and point were restored. This is a direct-relation-only pass; no Cut was created.

The first 1618 fr1 × fr2 stock attempt refused an extreme without a planar support Face. The later boundary-witness run passed for the actual 75/65 mm inclined-profile pair: two native Cuts, status 3/3, 22/16 relations, zero dimensions/planes, zero body difference and a 0.10 mm gap. Its actual planar/vertex witnesses were checked on trimmed native topology; the long Cut probe reported 23.117370 mm² exposed out of 240.645027 mm². This is an axial BUTT stock-copy result, distinct from reproducing the original miter.

A T2 positive attempt still refused shared-Part scope after duplicate occurrences were suppressed on a copy. No automatic Part-independence operation is implemented.

## Files

- [FLOW.md](FLOW.md): the existing Preview/apply flow and ownership boundaries.
- [REPRODUCE.md](REPRODUCE.md): independent harness sources, reproduction steps and test scope.
- [UNEQUAL_WIDTH.md](UNEQUAL_WIDTH.md): actual unequal primary widths and current qualification.
- [RULES.md](RULES.md): policy, geometric derivation and required KEEP checks.
- [SKETCH_CONSTRAINTS.md](SKETCH_CONSTRAINTS.md): actual relations and their evidence limits.
- [PERFORMANCE.md](PERFORMANCE.md): measured phases and remaining latency.
- [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md): failures, incomplete integration and unverified cases.
- [MANUAL_BUILD.md](MANUAL_BUILD.md): owner review and manual integration steps.
- [API_RESULTS.json](API_RESULTS.json), [API_RESULTS.csv](API_RESULTS.csv): structured evidence and test matrix.
- [SOURCE_VERIFICATION.json](SOURCE_VERIFICATION.json): completed source/protected-binary audit; main DLL not rebuilt.

No original CAD, B-rep serialization, DLL/PDB, persistence-reference token or private machine path is included. `API_VERIFIED` means observed API parameters; `GEOMETRY_VERIFIED` means native B-rep checks. Derived profile graphs and manufacturing interpretation are identified separately.
