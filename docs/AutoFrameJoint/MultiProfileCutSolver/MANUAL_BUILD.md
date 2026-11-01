# Code Owner review and manual Build

## Before integration

1. Read [API_RESULTS.json](API_RESULTS.json), [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md) and the final source audit. Confirm which code is an integrated candidate and which remains a copy-only experiment.
2. Preserve the user's current branch, project/deployment configuration and unrelated uncommitted edits. Apply reviewed changes against the matching local baseline; do not replace whole classes from a different baseline.
3. Keep the current A/B selection path and the existing CommandEntry → SessionManager → GhostPreview chain. Do not create another command or Preview system.
4. Inspect the complete source diff and `git diff --check`. Confirm only intended source/report files are included and no original CAD or binary is staged.

The root reports exact-baseline patches applied to three classes in both local and branch source; the SelectionPage UI source is unchanged. The completed audit records final source hashes and the preserved binary identity in [SOURCE_VERIFICATION.json](SOURCE_VERIFICATION.json). The existing files to review are `AutoFrameJointGhostPreview.cs`, `AutoFrameJointGhostSessionManager.cs`, `AutoFrameJointCommandEntry.cs` and `AutoFrameJointSelectionPage.cs`. Added helpers are intended to stay inside existing included source files. This report does not authorize a project/output-path/deployment change.

## Owner-run Build and smoke test

The agent has not built or deployed the main Add-in. After review, the owner may Build manually using the existing configuration and deployment process.

1. Start SOLIDWORKS with the intended DLL. Capture actual loaded module path, MVID and PDB match; do not identify a binary by its filename alone.
2. Use independent disposable assembly/Part copies first. Select A/B through the existing page and run Preview.
3. Test 2354 fr04 × fr03 as the legacy U regression, then fr02 × fr01 as the new folded BUTT case. Confirm the displayed result, rule, gap and exposure warning are understandable.
4. Press native OK and inspect actual Feature Tree, support Face, Sketch relations, constrained status, dimension/plane counts and final bodies. Record this as an installed Add-in test separately from the API harness.
5. Test Cancel/Esc, changed selection, stale Preview and refused rule cases. They must not apply an old or unqualified Cut Plan.
6. Exercise failure/rollback on copies, including any integrated cross-Frame references. Inspect geometry/tree, dangling references, document dirty state and Undo behavior.
7. Rebuild and change representative stock/profile/placement parameters on copies. Check for dependency cycles and altered intended clearance. Perform flat-pattern and downstream manufacturing checks.
8. Save deliberately only after inspecting the intended changes. No automatic Save is part of the tested writer contract.

The cross-end adapter is not integrated: its owned Undo qualification failed, and no Undo was attempted. Do not enable that experimental adapter based only on the positive stock-length test. Do not represent the cross-reference experiment or legacy U writer as complete before their remaining work is incorporated and tested. New installed interaction logs, source hashes and binary identity should be added to the same report series.
