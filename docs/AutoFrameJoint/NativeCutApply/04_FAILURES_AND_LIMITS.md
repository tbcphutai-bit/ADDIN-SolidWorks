# Failures, safeguards and limits

## Evidence boundary

Read [API_RESULTS.json](API_RESULTS.json) and [TEST_MATRIX.csv](TEST_MATRIX.csv) for the final run identifiers, cases, source variant and measured values. A result applies only to the case actually executed. Results from stock setup, existing finished specimens, and managed controls must remain separate.

| Evidence | What it establishes | What it does not establish |
| --- | --- | --- |
| Managed controls: 47 per source variant | Managed callback, lifecycle and refusal behavior exercised by the detached control harness | Native SOLIDWORKS geometry, installed DLL integration or visible Preview |
| Final native 2354 branch/public session-manager path | Existing Preview/session path created four real Cut features; final bodies matched the reviewed temporary bodies; input/copy file hashes remained unchanged | A user pressing OK in the installed Add-in |
| Final native 2354 injected final-validation failure | Four Cut features were created across both Parts before failure; owned-feature rollback restored both checked bodies and feature-tree sequences; input/copy file hashes remained unchanged | Restoration of the original Undo stack or save/dirty state |
| Final native 2354 stale-ticket refusal | No Cut was created for the tested stale ticket; input/copy file hashes remained unchanged | Every possible external document change or COM reentrancy sequence |
| Final native 3615 T1 local-source run | `FAIL`: feature query returned `0x80010105` (`RPC_E_SERVERFAULT`) before the native writer; input/copy file hashes remained unchanged | A permanent-Cut pass on the final local source |
| Final 3615 T2 suppressed-stock case | `NOT_RUN`: the batch stopped after the T1 failure | No pass or failure of the T2 native writer is inferred |
| Installed Add-in OK/Cancel interaction | `NOT_RUN` for the new handler | No claim that the installed DLL contains or executes the new handler |
| Flat-pattern/unfold/refold compatibility | `NOT_RUN` | Sheet-metal body classification alone does not prove downstream flat-pattern correctness |

The native public-manager case uses `TryShowCutCandidate` followed by `TryApplyCut`. It exercises the existing backend integration and native Display3 call path through an API harness. It does not verify user-visible Preview placement, smoothness or the native PropertyManager OK callback in the installed Add-in.

The final T1 error occurred in the stock-setup `GetTypeName2` query before writer entry. Its HRESULT identifies the observed COM server failure; it does not establish a corrupt Part or a specific crash cause. An earlier T1 run created four native Cuts with zero measured final symmetric difference and a 0.10 mm gap. That is separate historical evidence and does not override the final local-source failure.

## Native API differences found during testing

### Feature type can be ICE

The tested native cuts returned `GetTypeName2() == "ICE"` with `GetTypeName() == "Cut"`. The writer records both values and qualifies this combination as a Cut. It still requires a readable extrusion definition and matching depth, end condition, direction and Normal Cut parameters. A name containing `Cut` is not proof of a Cut feature.

### Activation status 2 is a warning

`ActivateDoc3` returned the needs-rebuild warning on tested documents. The implementation accepts that specific warning only when the returned active document has conclusive native identity with the bound document. Other error bits or a different active document cause refusal. This allowance does not force a document rebuild.

### One Undo call was insufficient

A single `EditUndo2(1)` did not reliably remove all plane/sketch/Cut steps across sketch edit boundaries in native testing. The failure path now removes only feature IDs registered as created by the current AFJ operation.

Removal is bounded by the number of owned feature IDs. Before removal, it checks that the Part still has the recorded post-operation tree and update stamp, and that the original feature sequence is intact when owned IDs are excluded. It re-enumerates live features after deletion because removing a Cut can also remove its absorbed sketch. It then checks the restored body and original feature-tree sequence.

`RollbackVerified` therefore means **checked geometry and feature-tree sequence were restored**, or no mutation occurred. It does not mean the original Undo history, selection, save flag, dirty state, graphics state, configuration caches or every document property was restored. When these proof checks fail, the result is `ROLLBACK_UNVERIFIED`; the report must retain that failure.

## Save calls and files on disk

Neither the native writer nor the inspected harness calls a SOLIDWORKS Save or SaveAs API. The current evidence field is `SaveApiCalled = false`. This states which APIs the implementation called; it is not a claim that no other SOLIDWORKS behavior or loaded add-in wrote a file.

An early 3615 T1 stock-copy run demonstrated this distinction:

| Disposable file | Input bytes | Copy bytes after the run |
| --- | ---: | ---: |
| `AFJ_PAIR_3615-04-bh-T1-fr1.SLDPRT` | 514276 | 283902 |
| `AFJ_PAIR_3615-04-bh-T1-fr2.SLDPRT` | 422829 | 263001 |

Those two selected copy Parts changed SHA256 and modification time on disk. The assembly, base Part, fr3 and fr4 retained their input hashes, sizes and modification times. The original private input files retained their hashes. The precise cause and write boundary of the two changed copies were not verified by that run.

The final 2354 cases and the final T1 attempt retained both input and copy hashes. This does not erase the earlier T1 copy-write observation.

Historical `saved_to_disk=false` / `SavedToDisk=false` fields were initialized by code and must not be used as proof that files stayed unchanged. Use the independent before/after file hashes in each run. Keep the early contradictory evidence in the report even if later runs pass the disk-hash check.

## Supported scope and refusals

An intermediate harness attempt could not read an open CAD file using its original sharing mode (`0x80070020`). That was an evidence-collection failure, not a Feature Cut result. The reader now permits shared read/write access; cleanup is still attempted if the pre-close hash observation fails. The remaining test documents from that attempt were closed through API only after checking the exact test-session PID and their paths inside its disposable fixture folder. The final 2354 rerun passed with input and copy hashes unchanged.

The native writer currently maps the qualified `ORTHOGONAL_U_COVER` and `ORTHOGONAL_U_TALLER_COVER` subtractive prism plans. It requires a reviewed, current, single-use Preview ticket and a matching active assembly. It does not create an arbitrary cut for an unsupported profile or placement.

The path refuses or requires explicit qualification for:

- Read-only/view-only Parts, unresolved or unsuitable selected occurrences, and active sketch or in-context Part editing.
- A changed source, configuration, occurrence or transform after the reviewed Preview.
- Two selections referencing the same Part document, or a Part reused by another occurrence within the assembly scope.
- Unresolved subassemblies where reference scope cannot be qualified.
- Multiple independent Part configurations without an explicit cut scope.
- Unsupported rule IDs, non-subtractive operations or unqualified tool mappings.

Keep the tested outcome for each refusal in the matrix. A code guard without an executed native case is not a native test pass. A 3615 stock-copy test also does not establish behavior for the existing finished Part: stock setup suppresses pre-existing cuts only on disposable copies.

## Geometry and manufacturing limits

- The new features use fixed reference planes and native sketches generated from the reviewed plan. They are a snapshot of that joint placement; automatic associative updates after moving a Frame are not implemented or verified.
- Per-step and final checks compare the actual native body with the expected temporary Boolean result, check a single valid remaining solid and sheet-metal classification, and check final overlap and fit gap. Symmetric-difference volume within the stated tolerance does not prove all manufacturing or downstream modeling requirements.
- `AssemblyFitGap = 0.10 mm` and `SheetCutOverrun = 0.10 mm` remain distinct conventions. A full formed-profile mask or Bend relief is not automatically a one-sheet `t + 0.10 mm` cut.
- Native Normal Cut is limited to the qualified one-sheet depth policy and operation intent. The tested full-profile/Bend masks retain their explicit B-rep construction; enabling Normal Cut globally would change that geometry.
- No new claims about all 42/43 historical Cut Features, all frame families or all joint angles follow from the new permanent-Cut cases. The earlier Feature Cut research and the new writer tests are different evidence sets.

## Delivery limits

No computer use, main Add-in Build/Rebuild/F5/Deploy, project/output-path modification, original CAD upload or merge was performed for this work. Detached compilation and API harness execution do not update the installed Add-in DLL. The Code Owner must review the source and then perform the manual integration and Add-in interaction checks described in [05_SOURCE_DLL_AND_MANUAL_INTEGRATION.md](05_SOURCE_DLL_AND_MANUAL_INTEGRATION.md).
