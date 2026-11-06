# Manual integration into the current R50 source

## Source variants

The Draft branch is based on PR #41, which already contains PR #40's source layout and R50 UI repair. Its only production modification is `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`.

The user's actual checkout contains an older backend. A separate method-level integration preserves its existing original-body overlay and Display3/Hide/session cleanup. The local-compatible patch and its before/after hashes are provided under `evidence/`. The original byte backup stays private. Apply the variant appropriate to the source baseline; the compatibility patch is a zero-context diff and requires `--unidiff-zero` for Git checking/applying.

The local-compatible backend has already been applied to the user's checkout after a hash guard. Do not apply the patch a second time. Only `AutoFrameJointGhostPreview.cs` was written; branch/HEAD, R50 UI and the other protected source/project files are unchanged. Installed DLL/PDB were not written by this task. The already-loaded Add-in is not changed by this source edit.

Do not replace unrelated current source with a PR branch snapshot. Do not change the working integration branch to obtain this file. No `.csproj` entry is required: all new internal types are in GhostPreview.cs.

## Methods and helpers to review

- `TryShowCutCandidate`: preserve entry signature; use the profile solver, validate before display, return friendly rule/geometry failures, keep modes 1/2 as KEEP and KEEP/CUT.
- Remove the obsolete private overlap/bisector path (`BooleanParts`, `SubtractMaterial`, `CreateBisectorHalfBox` and its report parsing helpers) from the older local variant.
- `AfjProfileAnalyzer`, `AfjParametricRules.Outgoing`, `VerifyPhysicalSkins`, `Generate`: inspect API profile identity, true stock midpoint, skin proof and height dominance.
- `AfjTemporaryBoolean.Validate/Boolean/Execute`: inspect copy ownership, error enum logging, no 547 retry, single-layer overrun guard and retained result validators.
- `AfjManufacturingConventions`, `AfjSheetCutTools`: inspect separate 0.10 mm meanings, nominal/tool depth distinction and rejection of unsupported Bend crossings.

`AutoFrameJointCommandEntry.Open`, session manager `TryShowCutCandidate`, public Ghost `TryShowCutCandidate` and UI `onPreviewCut` signatures are unchanged. Selection A/B, marks/filters and R50 lifecycle repair are unchanged. Existing UI labels P1/P2 remain; the backend modes now select KEEP versus KEEP/CUT display of the same verified rule, not alternative bisectors.

## Owner verification after review

No build/deploy is performed by this task. After reviewing the source and reports, use the owner's normal manual procedure in a healthy SOLIDWORKS session. Confirm the loaded module is the intended new build; editing a `.cs` file cannot change an already loaded DLL.

For 2354 select fr04 and fr03. Expect `[AFJ-R51] source=profile-skin-cutter`, profile-skin proof, `ORTHOGONAL_U_TALLER_COVER`, and two modified temporary Frames. Check the log body/volume/gap values against this report. Repeat both modes, reverse A/B, and close using the existing UI paths. Preview shifts are temporary and are cleaned with Hide.

Unsupported 1618 or insufficient-KEEP finished bodies should display a localized `UNVERIFIED_RULE` message and no candidate. A numeric 547 is reserved for diagnostic logs. If a native failure occurs, retain its exception/event and last operation label; do not continue using a process with known memory corruption.

Do not merge or deploy from the report automatically. General AFJ is not declared complete by these scoped tests.
