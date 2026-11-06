# Source, installed DLL and manual integration

## Source status is recorded separately from DLL status

Use [SOURCE_VERIFICATION.json](SOURCE_VERIFICATION.json) for the final local-source installation state, hashes and verification time. The four local-baseline patches were applied after the nine protected before-hashes passed their guards. The recorded after-hashes establish that source application; a GitHub branch or successful harness compile alone would not establish it.

At the pre-native-apply protection audit, all nine protected SHA256 values still matched the captured baseline:

- Six existing Auto Frame Joint classes.
- The main Add-in project file.
- The installed Add-in DLL and PDB.

The user's integration checkout remained on its existing branch and HEAD with unrelated uncommitted work. The project file and installed DLL/PDB retained their protected hashes after the four source patches were applied. No checkout, reset or replacement of unrelated work was required. Source application is not a deployment event.

## Installed binary evidence

The audited installed DLL had MVID:

`023f24dd-0f95-4cdd-9352-2629defdfab7`

Earlier R52 binary comparison tied that DLL to the Caption-fix version. It did not contain the newer R52 Preview-cache changes or this native permanent-Cut handler. Matching DLL/PDB evidence identifies that existing binary; it does not establish that newer source was compiled into it.

The final read-only metadata inspection confirms that same installed MVID and that `AfjNativeCutCommit` is absent from it. The detached native harness executables contain the type. The actual applied local files were separately compiled to a detached executable and passed 47 managed controls. See [assembly metadata](evidence/ASSEMBLY_METADATA.json), [actual local results](evidence/ACTUAL_LOCAL_MANAGED.json) and [source manifest](evidence/ACTUAL_LOCAL_SOURCE_MANIFEST.json).

No main Add-in build or deployment was performed here. Detached explicit-source harness executables were compiled outside the project for verification. Their successful compilation and native test results do not change the installed DLL, output directory or deployment configuration.

For the native tests, COM activation returned the existing blank session with process ID 7236; it was reused. ROT discovery had not exposed that session. This is a connection finding for those runs, not evidence that the new source was loaded into the installed Add-in.

If the owner has rebuilt the Add-in after this audit, capture a fresh DLL hash/MVID and loaded-module identity before interpreting a new interaction log. The MVID above is an audited historical binary identity, not a permanent assumption about future sessions.

## Four existing classes carry the integration

| Existing file | Change |
| --- | --- |
| `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs` | Adds the explicit apply callback to the existing page construction and reports its outcome. |
| `Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs` | Adds `TryApplyCut`, owns the apply lifecycle and hides temporary bodies before Part mutation. |
| `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` | Prepares the current reviewed Preview ticket and contains the appended internal native Cut writer/helper types. |
| `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` | Captures explicit OK intent, defers apply until `AfterClose`, and retains Cancel/Esc cleanup behavior. |

The helper is appended to an already included source file. No new project compile entry, `.csproj` edit, command registration, output-path change or deployment change is needed.

The existing entry points remain in use:

- `AutoFrameJointCommandEntry.Open`.
- `AutoFrameJointGhostSessionManager.TryShowCutCandidate`.
- `AutoFrameJointGhostPreview.TryShowCutCandidate`.
- The page's `onPreviewCut` callback and native A/B selection path.

There is one Preview/session path. The new apply action consumes the reviewed result from that path. Model mutation is deferred from native page closure to `AfterClose`; `OnClose` does not perform geometry work. Cancel/Esc do not invoke the apply callback. The retained R50 protection avoids reinstating the unsafe runtime native Label.Caption/Control.Enabled update pattern.

## Preserve the owner's baseline

Four local patches were generated from the captured local versions, separately from the four report-branch versions. They preserve existing baseline differences, including local P1/P2 wording, `StoreAnalysisResult` behavior and placement of `QueueCutCandidate`.

Consequently, a whole-class copy from a branch with a different baseline can overwrite valid local behavior. Use the local-baseline patch package when its recorded before-hashes match the actual source. Otherwise apply the reviewed method changes to the owner's current baseline and inspect the resulting diff.

The protection audit is a point-in-time check. Recheck all four before-hashes immediately before applying the local patches. Stop that copy if any file has changed; do not overwrite a newer edit using an old snapshot.

## Manual integration after Code Owner review

1. Review the source diff, [API_RESULTS.json](API_RESULTS.json), [TEST_MATRIX.csv](TEST_MATRIX.csv) and the rollback/save limitations in [04_FAILURES_AND_LIMITS.md](04_FAILURES_AND_LIMITS.md).
2. Confirm the current source baseline and the intended integration of the earlier R52 fixes. Apply the four existing-class patches to that baseline. Preserve A/B selection, native UI protection, command registration and unrelated work.
3. Inspect the complete source diff and run `git diff --check`. Confirm the project file, output paths and deployment settings remain as intended.
4. The owner may then Build the main Add-in manually using the existing configuration and deployment process. This step was not executed by the agent.
5. Start a clean SOLIDWORKS session using the newly built Add-in. Record the actual loaded DLL identity, MVID and PDB match. Confirm the source revision from the apply log and do not rely only on a DLL filename or timestamp.
6. First use disposable assembly/Part copies with independent references. Select A/B using the existing page, create the reviewed Preview, then press native green OK or the existing Apply-and-finish button. Confirm the page closes and the apply callback returns the native result.
7. Inspect the created plane/sketch/Cut features, body validity, final material, gap and interference. Verify user-visible Preview and native OK behavior in the Add-in itself. Record these as Add-in interaction results separately from API harness results.
8. Exercise Cancel/Esc and an invalidated Preview. Confirm they do not create permanent Cut features. Exercise the documented failure path on disposable copies and inspect the restored geometry/tree and document dirty state.
9. Perform flat-pattern/unfold/refold and downstream modeling checks before treating the feature as production-qualified. Inspect the changed documents and deliberately save only when the owner wants to retain the results.

The implementation makes no Save API call. That does not guarantee disk immutability in a session containing other SOLIDWORKS behavior or loaded add-ins; the observed early copy-file change is documented separately. Keep backups and compare file hashes in integration tests.

## Required distinction in the final status

For independent compilation, [CompileStandalone.ps1](evidence/CompileStandalone.ps1) takes explicit source, Interop, compiler and separate output directories. It compiles only the six listed source files into harness EXEs, runs managed controls and compiles the native harness without executing it. It does not invoke the Add-in project, post-build events or deployment.

The source can be reviewed and native API geometry can pass while the installed Add-in interaction remains `NOT_RUN`. The new native PropertyManager OK handler, actual user-visible Preview and installed DLL integration must not be marked complete from the 47 managed controls or public-manager API harness alone. Final source/DLL and interaction status belongs in the verification records linked above.
