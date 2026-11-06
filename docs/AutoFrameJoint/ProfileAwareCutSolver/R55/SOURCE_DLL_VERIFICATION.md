# Source, DLL and execution scope

## Exact tested source binding

Authoritative native audit SHA256: `a7b99697300b7156678fcc400cd51d085d31106b3f36b66a923729ca9543f642`.

| Qualified source file | SHA256 |
|---|---|
| R55RoleScopedDatums.candidate.cs | `d5176bd2403c63ffaf3529795177e32c11abce5ed1ce425ce15024a31d773a27` |
| AutoFrameJointSelectionPage.cs | `dd1506216efc372d82220e05fc5b0dec97898ab8c712910a6fdb6e03b01cd1e7` |
| R55FourCornerHarness.cs | `ee12bea89a35f18709758c4bbc866c7726a7186b6a2cf7e6398e0a1decafe3f1` |
| HarnessConnection.R55Owned.cs | `9a32fd5d1fbff525ed9c83d1306898ee4bd3a35058de0019bfd7e79e38b2baa9` |
| AutoFrameJointCommandEntry.cs | `4795f987af95740776f834cc454419bb50d557ab681f989452d6f2e35cdd3002` |
| AutoFrameJointGeometryMath.cs | `8678133ee61c94fb0d1262a33585803579760cb3b0f088c3bdc71142f04980f5` |
| AutoFrameJointGhostSessionManager.cs | `b505ce2611affaaa795d2246c8dd19560ee197dae8e7d79035bb89fc26d01fa1` |
| AutoFrameJointReadOnlyProbe.cs | `5b233792c7ea6d9042eddbdd2e5529be52dad354392bd8699ba2981cd6ef4a9b` |
| R55Native08RoleScoped.exe | `6d1ff8258747a919613bea0e2830d4f7593b41a78ed6aac12b2f972ffffa4e66` |

`SOURCE_MANIFEST.json` preserves the source/harness lineage, including a resumed exact checkpoint if used. Do not attribute inherited native PASS observations to a later source revision unless the lineage records that relationship.

All four native corner operations in this authoritative run started from the same fresh Assembly copy under the bound source revision.

## Failure, repair and resume lineage

Historical failed attempts 01 through 07 remain separate records in SANITIZED_FAILURES.json. Attempt 04 was rejected by the owned-process identity guard before CAD access; that observation does not establish a SOLIDWORKS crash. Attempt 05 retained two successful corners before a third-corner native relation-context refusal. The mixed-build resumed attempt 06 remained a failure: the exact previously retained repair orphan context 458 no longer appeared after new Assembly context creation, so the protected-history guard refused before Cut creation. That guard is not relaxed by a later result. Fresh attempt 07 also failed at the third corner's first perpendicular relation despite the active-Sketch binding change; that change alone was insufficient, and the failed attempt is not reclassified by a later successful run.

It also records 5 completed read-only/repair observations. SOURCE_MANIFEST.json and SANITIZED_FAILURES.json retain parent-report SHA256 edges and checkpoint-metadata hashes, verifying every declared parent hash before export. A read-only identity audit or restoration to a previous checkpoint is not a new joint PASS. Any exact known orphan-context metadata amendment affects only the current Assembly checkpoint; the original Assembly snapshot, Part Feature snapshots and material targets remain separately protected.

Observed known baseline identities omitted from public Feature enumeration while still resolving to their exact original native identities: **fr04 Feature 827**. This is metadata evidence, not permission to delete or replace the baseline Feature. Opaque native entity bytes, original machine paths and private geometry checkpoints are excluded from publication.

## Native relation-context cause comparison

Query-only diagnostic: `PASS_QUERY_ONLY_BODY_SKETCH_CONTEXT_UNCHANGED_NOT_JOINT_QUALIFICATION`, audit SHA256 `e3d64d13ca82ab52950f1b854a1158c3713f0b4abf32f1c07a1107264f48c72d`. The exact failed Sketch kept Feature ID 798 in both views, but SOLIDWORKS reported different native identities for its closed Part-context Sketch/profile and its active Assembly-context Sketch/profile. On the same corresponding support edge, the Part-profile alias advertised no allowed relation; the active Assembly profile advertised `PERPENDICULAR (8)` together with other valid relation codes. The comparison controlled the physical support edge, Sketch endpoint coordinates and relation count; they remained unchanged, and Body difference was zero.

This evidence requires binding the original segment IDs to the exact active Sketch context before adding relations. It does not justify bypassing allowed-relation checks, adding FIX dimensions, changing the cutter, or treating a Body-source change alone as a demonstrated fix. The probe itself created no relation or Cut; actual constraint creation and Cut geometry are established separately by the qualified native corner results. `RELATION_CONTEXT_DIAGNOSTIC.json` retains API arrays and identity comparisons without coordinates, opaque entity bytes or feature names.

## Native datum cache lifetime and later live query

The later one-scratch native experiment completed with exact restoration of all six copied document histories, dependencies and configurations, plus the five Part Bodies. The CreateLine-return segment was a different native alias from the active-inventory segment and advertised no relation for any of eight edge forms. Active-inventory and factory-specific inventory profiles accepted PERPENDICULAR on both cached and freshly mapped Part/Component edges; the closed Part-tree profile alias did not. This isolates another alias distinction, rather than proving that cached edges fail in every context. The scratch query added no relation or Cut and deleted its single owned scratch Sketch. See LIVE_DATUM_CONTEXT_DIAGNOSTIC.json for the full context matrix.

Versioned source review verified that the preceding native datum cache was shared across independent Cover, Loser and Cap Sketch roles and cleared only at writer completion. The role-scoped candidate discards Face/Edge/context handles after the preceding Sketch closes and before each current role reads its datums. Its rule, Cut Plan and cutter mathematics are unchanged in the recorded source comparison; existing identity and allowed-relation guards remain active. The `[AFJ-U-DATUM-ROLE]` marker records the refresh boundary.

Cache lifetime is a source-scope observation. The scratch query did not independently isolate stale cached edges as the cause of the failed integrated corner, and it supplies no native Cut PASS. Integrated results belong to the separate four-corner native qualification; prior failed attempts remain failed.

## Paired native verification of the observed fix

`PAIRED_CACHE_CONTEXT_FIX.json` records **NATIVE_VERIFIED_FIX_FOR_OBSERVED_THIRD_CORNER_TEST**. The failed217 and fresh D517 runs use byte-identical native inputs, identical Assembly poses/configurations and the exact same private third-corner seed/axis metadata and datum owner 512. The first perpendicular relation changed from an empty allowed-relation array to `[27, 8, 7, 14]`. The corrected corner created four fully constrained native Cuts with zero target B-rep difference; all four corners of the authoritative suite use the same fresh D517 build.

The versioned source diff contains only role-scoped datum-cache refresh, its preconditions and diagnostic marker; rule/cutter mathematics are unchanged. This verifies the applied fix for the observed fr03 × fr02 test. It does not convert the separate scratch query into a Cut test or establish a universal explanation for every COM context/lifetime failure.

## Main source installation

Status: `MAIN_TWO_SOURCE_FILES_INSTALLED_NO_ADDIN_BUILD_OR_DEPLOY`. Protected project/DLL/PDB and unrelated entry-point checks: `True`.

| Main source file | Installed source SHA256 |
|---|---|
| AutoFrameJointGhostPreview.cs | `d5176bd2403c63ffaf3529795177e32c11abce5ed1ce425ce15024a31d773a27` |
| AutoFrameJointSelectionPage.cs | `dd1506216efc372d82220e05fc5b0dec97898ab8c712910a6fdb6e03b01cd1e7` |

The user log observed loaded Add-in DLL MVID `5ec891d1-e8ff-4add-9cd0-60cadae6d10c` before this source delivery. That observation is not a proof that the current source has been compiled into that DLL. No main Add-in Build, Rebuild, F5, deployment or output-path change is performed. The user must manually Rebuild the main project and reload the resulting DLL before evaluating this source in the installed Add-in. Standalone harness binaries are separate from that deployment and are excluded from this report.

## Preview callback integration

The existing SelectionPage → CommandEntry → GhostSessionManager → GhostPreview → solver chain is retained. Deferred Analyze now requests the same existing KEEP Preview after a valid pair is analyzed; the existing reviewed-ticket guard still protects OK/Apply. The same-source Preview cache avoids repeating Solve/Hide/Display3 when unchanged. No second command or Preview subsystem is introduced.

The candidate UI harness ran **56 managed contract cases across two preserved UI baselines**. This result exercises managed deferred-flow/close guards with no native COM interaction; it is not an installed Add-in test. Native Body/Apply validation comes from the separate four-corner API run.

## Delivery checks after source installation

The actual six main sources and the actual six branch sources both passed detached compilation. This did not build the Add-in project. `DETACHED_COMPILATION.json` preserves both source sets and their distinct UI baseline hashes.

The deployed DLL remains SHA256 `9c340dc7461adc565677279c52de8d4b5cad1cc8b0384fd5614ee7bb1deee8ec`, MVID `5ec891d1-e8ff-4add-9cd0-60cadae6d10c`; metadata does not contain the new `BeginNativeDatumRole` method. The user must manually Rebuild and reload the main Add-in.

`SOURCE_GIT_BLOB_HASHES.json` binds both published C# files to their staged Git blobs and records whether line endings were normalized. No source formatting change was needed after native qualification.
