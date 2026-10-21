# Runbook 47 — Auto Frame Joint: Explicit Finish and cut-preview truth gate

## Owner / implementation

- **Author / Code Owner:** ChatGPT; Antigravity is **COPY ONLY**, not author.
- Exact source commit: `9421e10281fee11c555edf77aef801b9562ad1c7`.
- Full C# class: [AutoFrameJointSelectionPage.cs](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/9421e10281fee11c555edf77aef801b9562ad1c7/UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs).
- Source original Git blob: `4dcfe9fd835b5c0acc45bef351f5ae3e9c41701a`.
- Source updated Git blob: `f64d9eb6bc802e1317bad01f2257405d074d4c30`.
- Worktree TARGET: `C:\Tao moi\ADDIN\UI\AutoFrameJoint\AutoFrameJointSelectionPage.cs` in the ACTUAL shared Visual Studio project.
- Do not checkout or merge the source branch in the shared local worktree. This GitHub branch is only a source handoff; it does not include the user's uncommitted Check Bend/Check Hole updates.

## Root cause / observed host evidence

Observed native PropertyManager has only the red X. Source used the Cancel option, without a green OK/Finish button. There was **no explicit Finish control**.

Read-only host log for 2026-10-21 analyzed both Frame occurrences:
- READY=True, A-count=1, B-count=1, AUTO ANALYZE START/END.
- Angle 90 degrees; long B; short A; estimated finite-axis gap 2.455 mm.
- `JOINT_VERTEX_ENVELOPE_OVERLAP_CANDIDATE: YES` — this is only a candidate based on sampled vertex-envelope overlap, not a verified solid overlap.
- `Cut geometry: NOT GENERATED`.
- `Profile Web/Wall/Lip: NOT YET ANALYZED`.
- `Winner/Loser: UNCONFIRMED for this Assembly`.

**Therefore: this Runbook fixes the exit UI ONLY. It does not create or claim to create a preview of a cut or a real manufacturing cut.** Existing `Xem trạng thái A/B` shows original temporary body overlays, NOT a cut. Do not rename it as `Preview vết cắt`.

## ChatGPT-authored exact change

Only the original `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` is edited:
- Adds `ButtonFinish=106`, `finishButtonControl`, `finishQueued`.
- Native PropertyManager options now include `swPropertyManagerOptions_OkayButton | swPropertyManagerOptions_CancelButton`, showing standard green check and red X.
- Adds the explicit in-page button `Kết thúc (không cắt Part)`.
- Clicking in-page Finish queues `PropertyManagerPage2.Close(true)` using the existing WinForms UI-thread dispatcher **after** the native `OnButtonPress` callback returns. If dispatcher is unavailable, directs user to native green check instead of running unsafe callbacks.
- Existing `OnClose` marks closing, existing `AfterClose` invokes injected cleanup `onClearPreview`, releases GUI references, and then invokes `onClosed`. NO Apply Cut/save/new features.
- All other source files, `ADDIN.csproj`, Debug x64 output path and build events remain unchanged on the authored GitHub branch.

## Antigravity COPY ONLY: safe steps

1. In `C:\Tao moi\ADDIN` first check current local branch/status, and inspect `UI\AutoFrameJoint\AutoFrameJointSelectionPage.cs` without modification.
2. Run read-only `git hash-object -- UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`. Original accepted hash **must** equal `4dcfe9fd835b5c0acc45bef351f5ae3e9c41701a`. If it differs, **STOP** and report source SHA and diff; do not overwrite other local changes.
3. Copy the **FULL ChatGPT-authored class** from exact immutable commit `9421e10281fee11c555edf77aef801b9562ad1c7` and paste into the **existing original local file** `UI\AutoFrameJoint\AutoFrameJointSelectionPage.cs`. NO new class, NO extra sidecar, NO compiler project manipulation. Do not write your own variation of ChatGPT C#.
4. After copy, run `git hash-object -- UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` and check SHA `f64d9eb6bc802e1317bad01f2257405d074d4c30`. If BOM/CRLF differs, report exact diff/byte facts; never force-replace files just for predicted hashes.
5. Verify read-only: new `ButtonFinish=106` exactly once; `swPropertyManagerOptions_OkayButton` exactly once; explicit `Kết thúc (không cắt Part)` exactly once; `current.Close(true)` exactly once; `AfterClose` still invokes `onClearPreview` then `onClosed`.
6. Report actual local `git status --short`, diff for only this C# file, original/result SHA, other files touched by THIS task (must be none). Pre-existing changes in Check Bend/Check Hole are not to be discarded, staged, committed or touched.
7. STOP. No build, rebuild, F5, deploy, DLL/PDB writes, COM registration, SOLIDWORKS process manipulation, git checkout/reset/clean/stash/merge/push/commit. User alone decides manual Build and SOLIDWORKS host test after backing up currently installed DLL/PDB.

## Acceptance / host test for USER ONLY (later)

After the user's manual x64 Debug build and launch:
- A/B selection and automatic read-only Analyze still operate.
- Standard native green check and explicit `Kết thúc (không cắt Part)` both close the PMP.
- Existing red X remains Cancel.
- Closing clears any displayed temporary Ghost bodies using existing managed cleanup, leaving original Part/Assembly and configurations unchanged; close may retain pending Ghost cleanup if SOLIDWORKS Hide fails, which must be reported rather than claimed clean.
- Reopening Auto Frame Joint is possible without stale session.
- No new cuts or modifications appear in model or feature history.
- If `page.Close(true)` raises COM error or AfterClose does not fire, capture `[AFJ-PMP]` and `[AFJ-GHOST]` Output and return to ChatGPT.

## Independent next requirement: REAL cut preview (NOT IMPLEMENTED IN RUNBOOK 47)

The request is to visualize exactly **which surfaces/edges of real Frame solids would be removed**, e.g. profile Web/Wall/Lip differences. This requires a verified manufacturing cut rule and the real 3D body geometry in the correct Component transforms; axis-nearest point and a generic 45-degree plane are insufficient. No code should falsely display a guessed plane/ghost as the completed cut.

Before ChatGPT authors this separate safe, read-only temporary-geometry preview, obtain from user:
- Which of Frame A (1500 mm, SHORT) and Frame B (3200 mm, LONG) must retain material in THIS assembly, or a drawing specifying the winner/loser.
- Section family/profile (Web/Wall/Lip with protruding lip) and required end/notch shape, preferably user-marked cut line in screenshot or drawing.
- Whether drawing overrides any generic short/long rule.

Planned preview acceptance (later phase): show colored **actual proposed kept solid and removed region** as independent SOLIDWORKS temporary bodies, highlight intersection/cut contour, allow toggling original-versus-after, and clean up on cancel/finish; never change Part/Assembly/FeatureManager or auto-save. Report `UNVERIFIED_RULE` if the rule is missing; do not show a fabricated cut.

**Runbook 47 result = FINISH UI SOURCE AUTHORED. CUT PREVIEW NOT YET AUTHORED. BUILD NOT TESTED.**
