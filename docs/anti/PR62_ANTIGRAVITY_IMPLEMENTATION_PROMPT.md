# ANTIGRAVITY — PR #62 CHECK DRAWING 表/ウラ: ONE CONSOLIDATED IMPLEMENTATION

**User authorization:** Implement scoped CHECK DRAWING changes on the existing branch, commit/push, report for ChatGPT review. Do not merge.

- Repository: `tbcphutai-bit/ADDIN-SolidWorks`
- Branch: `fix/check-drawing-goban-display-retry-uraomote-20261108`
- PR: https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/62
- Source base for patch review: `cfdfa94be9b1d76ac92714016b9e8e9677f9532a`
- User environment: SOLIDWORKS 2024 SP5.0, Debug x64, .NET Framework 4.8.
- Read these files on the PR branch **before making code edits**:
  1. `Commands/CheckDrawingBom.cs`
  2. `Commands/CheckUraOmote.cs` (READ ONLY; do not change)
  3. `Commands/CheckPartWorkingCopy.cs` (READ ONLY; do not change)
  4. `docs/reports/CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md`
  5. `docs/anti/PR62_FlatSidePilotPolicy.cs.txt` (**actual C# implementation**)
  6. `docs/anti/PR62_FlatSideIntegration.cs.txt` (**exact integration snippet**)

## What the user is trying to fix

The 55-row CHECK DRAWING batch currently shows `11 OK, 0 NG, 44 WARNING`. Of 50 Drawing Views that have a 表/ウラ Flat Pattern check, 37 Working Copies fail because in-context reference Assemblies are already Dirty. Shadow direct read classified 43/50; 32 of the 37 blocked could potentially get a real face verdict; five remain UNKNOWN. Shadow has not converted ANY production status.

**The goal is NOT to hide warnings.** Correct Drawing marked 表 should pass when the *correct displayed side* is proven; incorrectly flipped views/labels must fail NG; ambiguous source/config/color/dirty-state must remain WARNING.

Previously reported caveats:
- In `1B_TR11_rn1_DISAGREE_CHECK`, legacy Working Copy sees BOTH front & back pink (`42109.7/42239.0 mm2`), yet its `ClassifyFlatPinkSide` returns `true` (ウラ), because it only checks `front`. This is an ambiguous legacy verdict. Do not alter the shared standalone `CheckUraOmote.cs`; guard the result only inside CHECK DRAWING.
- `1D_4332a_P1_l1` multiple possible Flat Pattern Views. Never assign a label to the wrong view.
- Silent/hidden opens reported zero visible entities (`GetVisibleEntities2(...)=0` on 50/50). `HIDDEN_DS_FACE_BODY` inferred a body from face-specific display-state appearances on 43/50. Planar symmetry and single-body inference alone do NOT prove the cached body corresponds to the actual Drawing View or that external in-context edits are fully resolved.
- New code must never skip file ownership/Dirty safeguards or save/close user documents.

## Implementation — use provided actual code, not just a plan

1. Compare current HEAD to `cfdfa94`; never blindly overwrite a newer version. Copy `PR62_FlatSidePilotPolicy.cs.txt` into an actual compiled `Commands/CdFlatSidePilotPolicy.cs` (register in `ADDIN.csproj` if it is old-style explicit-include project). Or integrate in `CheckDrawingBom.cs`, preserving class namespace. This contains the complete `CdFlatSidePilotPolicy.Evaluate` logic and `RunSelfTests`.
2. Use `PR62_FlatSideIntegration.cs.txt` for the exact replacement around `AddFlatPatternSideChecks` and three scoped helpers. That integration is intentionally conservative and works while its pilot flag is OFF.
3. **Crucial: finish provenance as genuinely grounded code.** `CdBuildCandidateFlatSideProof` deliberately sets `FlatPatternBodyVerified`, `FaceColorCompleteAndUnambiguous`, and `SourceFreshnessVerified` to false until you can substantiate them. Do not flip them to true without positive evidence. Instrument the Direct checker to return a structured evidence object (not parsing its Vietnamese message): referenced Drawing/Part exact paths, view name + sheet, requested vs active config, display state, one matching flat-pattern body and all visible planar faces, effective Face>Feature>Body>Model color source on BOTH sides, dirty flags and freshness proof.
4. The freshness condition is the hardest part. A file hash unchanged during the probe, `GetSaveFlag()==false`, and symmetric planar body DO NOT prove a hidden Drawing View represents current in-context geometry when a parent Assembly has unsaved edits. Devise a safe **independent** check or explicitly mark freshness UNVERIFIED and keep WARNING. Do not set a constant `true` or assert semantic proof from a saved .SLDDRW alone.
5. Legacy both-sides-pink is AMBIGUOUS; choose Unknown when pilot evaluates it, regardless of which side the old checker returned. If Direct and truly determinate Working Copy disagree, force WARNING and log `DIRECT_WORKCOPY_DISAGREE`.
6. Only consider an explicit narrowly scoped pilot promotion when legacy failed **specifically** on a Dirty parent Assembly and Direct evidence is fully verified. The template's localized warning string check is temporary; create a structured classifier at the CHECK DRAWING boundary before allowing promotion.
7. Keep default `CdEnableReviewedFlatSidePilot = false` and existing `CdEnableDirectFlatSideVerdict = false` in user-facing builds. For any pilot, make a separate isolated test-only build/flag with a clear deliberate opt-in and rollback. Never silently enable globally.
8. No unconditional conversion of WARNING to OK/NG. Preserve `CompareFlatSideMarker` polarity: `表 + nonpink = OK`, `ウラ + pink = OK`, mismatches NG, unknown WARNING. All other checks (W/L, 合番, 数量, 材質, 板厚, Excel on click, UI) unchanged.
9. Perform a full diff review; only CHECK DRAWING source and explicitly required project registration + `docs/anti` / `docs/reports` changed. Do NOT modify standalone `CheckUraOmote.cs`, `CheckPartWorkingCopy.cs`, Check Same Part, DXF, Check Hole, or unrelated commands.

## Verification / acceptance

### Build and deterministic tests
- Debug|x64 full build; give exact build command, result, warnings, DLL path and SHA-256.
- Run `CdFlatSidePilotPolicy.RunSelfTests()` and explain that these are synthetic policy tests, NOT COM verification.
- Regression: 合番, W/L, 2D/3D, Preview vs on-demand Excel, no accidental old UI change.
- Verify policy with: shadow OFF; clean agreement; determinate disagreement; WC both sides pink; dirty Assembly + complete proof; dirty Assembly + stale/incomplete proof; unrelated WC failure; Part dirty; Drawing dirty; multi-body; no paint; many views.

### Live SOLIDWORKS 2024 SP5 tests on ISOLATED COPIES ONLY
- An authentic normal 表 view + authentic ウラ view, actual `FlipView` reversed view, inverted marker, painted BOTH sides, missing paint, feature/body/model color inheritance, and conflicting appearance priority.
- The two disagreement drawings `1B_TR11_rn1` and `1D_4332a_P1_l1` need independently auditable face orientation and effective color evidence, not just the two algorithms' claims.
- Dirty Assembly without geometry change, Dirty Assembly WITH unsaved geometry/face appearance/in-context change, Part dirty, Drawing dirty. The new logic must never silently turn a stale-view situation into OK.
- Run full 55-Drawing batch in shadow, and any narrowly scoped opted-in pilot separately; report before/after exact status counts (not projected).
- Log `[CD PILOT DECISION]`, `[DIRECT_WORKCOPY_DISAGREE]`, `[FLAT DIRECT BODY]`, `[FLAT DIRECT OK/UNKNOWN]`, `[CD ASM SNAPSHOT]`, `[FLAT SIDE NOTE]`, `[BATCH CLEANUP]`. Log a per-view provenance record, reason codes, body method `VISIBLE_ENTITIES2` vs `HIDDEN_DS_FACE_BODY`, source freshness proof and which program outcome was used.
- Before/after exact open-doc path list and dirty/read-only state; file checksums/timestamps of Drawing, Part, Assembly; no unsolicited Save/Rebuild/Close. Native crash and COM exceptions must be recorded honestly. Never overwrite locked `C:\Tao moi\ADDIN.dll`; close SOLIDWORKS safely before rebuilding/deploying.

### Result categories
- `OK`: independent evidence proves true physical side agrees with marker.
- `NG`: independent evidence proves true physical side opposes marker.
- `WARNING`: source stale, side ambiguous, both sides pink, missing paint, incorrect config/path, unproven hidden body, legacy/direct conflict or failed evidence.
- `N/A`: only where no flat-side check is applicable per existing rules.

## Required GitHub deliverables from Anti

1. Commit scoped functional code (NOT only these template docs) to same branch of PR #62. DO NOT merge.
2. Update `docs/reports/CHECK_DRAWING_URA_OMOTE_PR62_REPORT.md` with:
   - repo/branch/head SHA and exact changed files/functions;
   - explanation of `1B` and `1D` disagreement;
   - how hidden flat body and freshness are actually verified or why BLOCKED;
   - 55-row field/status matrix; 32 candidate rescues vs actually promoted; remaining five dirty-ASM unknown;
   - real log references/paths + DLL hashes; full build/test commands and outputs;
   - source file preservation, before/after ownership and Dirty states;
   - explicit PASS / FAIL / NOT TESTED / BLOCKED tags for each acceptance criterion;
   - risk list and recommendation whether a controlled pilot can start.
3. Post the report link and commit SHA as a PR #62 conversation comment. The final message to the user should contain:
   - PR link;
   - new commit SHA;
   - direct link to report Markdown;
   - build/test results;
   - whether shadow or pilot is enabled, and exact reason;
   - any unsupported environment or missing test.

**DO NOT CLAIM COMPLETE OR NO CRASH WITHOUT ACTUAL LIVE EVIDENCE.**

### Important note about provided code

The two `docs/anti/*.cs.txt` artifacts are real C# code templates, but they are **not** compiled or deployed just because they are on GitHub. The deliberately false proof flags are essential fail-closed placeholders requiring a defensible implementation. Anti must integrate, build and exercise them on isolated copies before proposing any pilot. Never modify them to blindly force OK.
