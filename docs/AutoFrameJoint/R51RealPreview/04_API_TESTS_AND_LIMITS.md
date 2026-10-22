# API execution and limits

## Separation of tests

1. **Source identity:** hash the local classes/project/output binaries, read installed DLL MVID, inspect Git state, compare local R48 backend with PR #40/#41. No assembly code is executed merely to read its MVID.
2. **547 reproduction:** temporary copies from the private assembly; log all operation error enums and input/intermediate validity. One edge-tolerance reset trial only. Preserve failures.
3. **Native Feature audit:** 42 Cut scalar/Sketch records on 11 new Read-Only copies, with byte hashes; compare with previous extraction. Historical 42 before/after snapshots are not replaced.
4. **2354 suppressed history:** three Cut state changes on two separately named copies. Restore suppression and close without save. Record all strict-volume failures.
5. **Runtime acceptance:** compile the proposed local backend into a detached x64 STA executable. Solve 2354, two 1618 pairs and eight 3615 pairs. Measure actual kept/removed bodies, not a success flag alone.
6. **Native replay:** use saved native before bodies and research-only tool oracles to compare 28 corner after bodies. Those templates and correction cells are absent from production rule selection.
7. **Independent controls:** one-layer t+0.10 mm, incorrect direction/Bend/extra-layer refusals, dimension/placement/A-B reversal variants and covering-height negative cases. Individual result JSON is authoritative; an expected refusal is not a cut Preview pass.

## Acceptance requirements

`PASS_GEOMETRY` requires valid temporary solids, Check2/Check3, one kept solid per member, conservation, required KEEP coverage, confinement to the selected end, negligible interference and measured minimum gap within the declared tolerance of 0.10 mm. The 2354 case also requires positive removed volume on both Frames.

`PASS_DISPLAY3_API` requires the public `TryShowCutCandidate` to return success with tracked displayed bodies; both KEEP and KEEP/CUT modes are exercised and cleanup is checked. It does not prove human visual fit or correct installed UI behavior.

`UNVERIFIED_RULE`, `FAIL`, `NOT_RUN` and non-corner exclusions are kept separate. No 547 is converted to PASS. No successful result is inferred from compilation, exception handling or displaying original bodies. If a pair already needs no cut, the backend returns `NO_NEW_CUT_REQUIRED` instead of presenting it as a newly cut result.

## Existing finished bodies

The 3615 finished bodies were authored under historical rules. All eight current-state attempts are refused where the new policy requires material that is already absent. Their native before-Cut snapshots pass, and public Preview passes on privately rolled-back copies. The rollback is test setup; production Preview does not roll back or unsuppress Features.

1618 fr1×fr2 and fr3×fr4 remain unsupported by the unique-Web/parallel-Wall classifier. Their real API attempts and localized public refusal messages are recorded; no new cutter is displayed. Replaying an individual historical 1618 Cut does not establish a generalized pair rule.

The first old Debug pair is the now-tested 2354 fr04×fr03. A second distinct old Debug pair remains `NOT_IDENTIFIED / NOT_RUN`, as recorded in PR #40.

## Final-source and control results

The 11-fixture acceptance matrix used the earlier R51 local source hash recorded in `HARNESS_PROVENANCE.json`. Subsequent changes add the single-layer stock guard, depth-policy tags and comments; the final local and Draft-branch variants each re-run 2354, including A/B reversal, both public display modes and Hide. Final hashes and full metrics are published separately. This does not relabel the older matrix as an execution of a later binary.

Twelve parameter controls pass. Three additional plain-U taller-cover controls pass with Check2/Check3, volume, gap, interference and A/B reversal; the first places the profile at a one-sided stock end and applies rotation/translation. Two lower-Wall controls produce the expected `UNVERIFIED_RULE`. Two planar single-sheet controls pass; incorrect skins, Bend crossing and a second nearby sheet are refused.

The extra-sheet harness first failed during `CreateBodyFromBox3` fixture setup with `0x8002000D / DISP_E_ARRAYISLOCKED`, before executing the cut plan. Its failed result is retained. The harness fixture was then constructed using the same prism factory used by the other controls; that run reached and passed the expected stock-guard refusal. No production validation was relaxed.

## Native and tool limits

The added single-sheet factory supports a verified planar layer and refuses an unhandled Bend crossing. The formed U plan supplies explicit Bend-relief masks. Arbitrary sheet NormalCut surface development, inclined/step/double-Lip sections, unequal t/R, lower covering Walls, mismatched Returns, mirrored/scaled placement, nested components and unresolved configurations remain outside verified scope.

The historical 2354 sequence differs from the current shorter-cover result, and two native volume residuals remain unresolved. See [2354 comparison](03_KEEP_CUT_AND_2354_RULE.md).

## Build and model protection

This task writes no `.csproj`, build path, DLL/PDB, Check Bend, Check Hole or other command. No original CAD/STEP is edited, suppressed, saved or rebuilt. Detached harness compilation uses explicit source files and references, without the main project or its build/deploy steps.

DLL/PDB hashes differed from the initial snapshot **before** the local source patch. Their hashes remain identical immediately before/after this task's source application. Hashing does not establish the external writer/cause. All protected source/project files matched the initial snapshot, with only the intended backend changed by application. See `evidence/SOURCE_PROTECTION.json`.

The main Add-in is not built/rebuilt/F5/deployed. One detached compile includes the six AFJ classes to check callback/signature compatibility; it is not an Add-in build or installation. The actual R50 UI source and backend ownership/cleanup methods are preserved.

Only files opened by a harness under guarded private paths are closed by that harness. `ActivateDoc3` uses `swDontRebuildActiveDoc` where required. Another user's existing document/application session is not closed. No application activation fallback is used in the final harnesses.

Source/application protection and test provenance are published with sanitized filenames/hashes. Full private file paths, raw CAD streams and binaries remain local.
