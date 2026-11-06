# Manual copy and Build by the Code Owner

No main Add-in build or deployment was performed. This PR targets the R49 preview branch, whose baseline is 423300132a4011a1e534b5c98adf94ceeef02269. Review its parent dependency before integrating with another branch.

1. Preserve/commit the current user's work first. Compare the four source files against the target checkout. Do not overwrite unrelated A/B selection changes with an entire older/newer UI file.
2. For a target already at R49, apply the four-file PR patch. All new solver helpers are inside Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs; no .csproj change is needed.
3. For another target, integrate the Ghost `TryShowCutCandidate` replacement, new internal helper types and required System.Linq/System.Globalization usings. Keep its existing constructor/`TryShow`/display/cleanup and the original command/session entry points. Apply only the two button-label/tooltips and status-text changes in UI BuildControls/QueueCutCandidate. CommandEntry and SessionManager functional wiring were not changed by this PR.
4. Inspect the exact changed files and the unsupported-rule contract. The old P1/P2 button IDs are retained but now mean KEEP and KEEP/CUT for the same geometric plan.
5. The Code Owner may manually build the main Add-in in the established .NET Framework 4.8 / C# 7.3 / x64 environment, then load it by their normal workflow. This document is an instruction for the owner, not a record that a build was run here.
6. Open a copied assembly in the correct folded configuration, select A/B using the existing selector, choose KEEP/CUT, verify shorter-cover policy, 0.1 mm gap and refusal outside the supported topology, then clear/close/switch document and check ghost cleanup. Installed PMP selection/deferred callback behaviour remains a manual host test.

The standalone harness is a separate STA executable and never references the Add-in project file or runs its post-build registration/deploy steps. Numeric reports can be reviewed without any CAD upload. No binary, native CAD or machine-specific path is included in this PR.
