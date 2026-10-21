# CHECK CHẤN — ONE-PASS COMPLETE REPAIR CONTRACT

Date: 2026-10-21. Base: Draft PR #38, branched from PR #37. THIS DOCUMENT SUPERSEDES all piecemeal V4.x UI task prompts. **Do NOT automatically Build/Rebuild, run SOLIDWORKS, produce DLL/PDB or merge main.** The user will manually Debug/x64 once the comprehensive source work is ready.

## A. The user-facing product

Operator workflow: select Part/Assembly → Bắt đầu kiểm tra → click Part/B1..Bn → look at one large actual CAD 2D cross-section and ONE trustworthy result. Only open technical details if needed. When backed by real numeric collision evidence, show exactly which Part edge and Punch or Holder segment touches/overlaps. Otherwise show an honest, short reason and UNKNOWN. Do not show all 18 UNKNOWN lines by default.

Default layout: left ~22% Part/bends; center ~53% actual section; right ~25% result. Right result: one verdict; Check Min; Check MAX / Excel; Collision; optional single source-configuration recovery action. Move all tool candidates, geometry evidence, source hashes, W/L sources, long English failure codes, sequence candidates, and debugging controls into collapsed detail tabs. Preserve all existing data and event handlers, without creating another versioned UI class file.

## B. Proven root issues (real Output-Debug.txt)

Three RunAsync / BindSnapshot cycles returned 5 Part entries. Source records show ToolCount=18 DEFAULT IDs, DataCount=0, MatchedPairs=0, NumericQueries=0. User's running SOLIDWORKS process had no file C:\Users\SGN26\AppData\Local\ADDIN\bend-tool-dxf-source.json and no environment override. The manager performed 38 ToolLibrary.Read calls; ToolProfileLibrarySession.Poll rereads every 2 seconds, even while source config is missing. The 18 listed IDs are **not** 18 loaded punch/holder contours or 18 checked tools.

Two bends FeatureId 524 and 530 had Check Min MeetsMinimum, not Check MAX. Unsupported bends/Part need a distinct UNRECOGNIZED state, not an ungrounded 0 bends conclusion. Existing code only transcribes 5 Excel family length references; does not implement a certified Excel-case matcher. Existing preview captures final-Part sections but no proven Punch+Holder mounting transform; a saved-file thumbnail is not real collision output.

Do not attribute the failure to CAD corruption, COM, or third-party XTool WPF data binding errors without evidence. A missing settings file is proven; how it disappeared relative to earlier successful managed reads is not yet proven.

## C. Original Excel and CAD facts — locked (do not reinterpret)

From user's original 深曲げ限界(4).xlsx:
- 金型限界表 B3:B11: サッシ型, HDS/HD, 6000 mm supported length.
- 金型限界表 J3:J11: グース型, HDS, 4200 mm supported length.
- 金型限界表 R3:R11: 特グース型, HDS, 4200 mm supported length.
- 金型限界表 B33:B41: 直剣型, HDS, 4200 mm supported length.
- 金型限界表 J33:J41: グース型, HD, 6000 mm supported length.
- 深曲げ限界 N47: condition that die/equipment do not interfere; C48:G48 shows ST3.2, SUS3.0 6000 examples.

W/L in illustrations represent cross-section shape/depth constraints, NOT the 4200/6000 bend-line supported length. Illustrative orange/red strokes are NOT certified CAD limits and must not be measured by screenshot pixels.

User's 18-company Punch–Holder original CSV: exactly 18 pairs and original handles; 17 punch outlines marked closed, KIK-7181328001 deliberately OPEN. Group mapping counts: SASH 3, GOOSE 4, SPECIAL 1, STRAIGHT 4, GOOSE_DEEP 1, OTHERS 5, 18/18 Physical_Fit_Qualified=NO. Grouping is a visual Excel family suggestion, NOT approved Excel-case-to-ToolId compatibility or physical holder seating. Holder means UPPER CLAMP, NOT lower V-die. No edit/close/rotate/scale original CAD, do not upload company Excel/DXF/STEP/CSV/config to GitHub.

## D. EXACT evaluation pipeline: run once per current snapshot

1. NATIVE INVENTORY: match Part file + configuration + active FeatureId; extract bend length, material/thickness, actual W/L section, suitable state geometry and coverage. Distinguish no bends vs unsupported recognition vs stale/canceled.
2. INPUT HEALTH: once per run, immutable source snapshot for Excel fingerprint, original tool source config, 18 declared pairs, count of valid Punch+Holder geometry, datum/holder confidence and current work-plane registration. ToolCatalogCount is distinct from ReadyGeometryPairs, VerifiedMountingPairs and NumericEvaluatedPairs.
3. EXCEL FIRST: case applicability must be tri-state MATCH_CONFIRMED / NOT_APPLICABLE_CONFIRMED / MATCH_UNRESOLVED, based on real geometry, material, system, verified family and exact source. If MATCH_CONFIRMED and certified numerical limits exist, compare relevant W/L/length, show value and source case. If MATCH_UNRESOLVED, show Excel example plus UNKNOWN; DO NOT pretend no case and jump to an approved CAD result. Only NOT_APPLICABLE_CONFIRMED enters alternative CAD fallback.
4. CAD FALLBACK: needs same real Part section coordinate frame, actual CAD Punch and Holder, reviewed work placement/mounting, proper before/during/after states and numeric contact witnesses. If missing, UNKNOWN. Only witness-backed Part segments may be painted RED; distinguishing verified Excel LIMIT FAIL from verified CAD COLLISION. A nearby segment or apparent contact on a sketch is NOT collision evidence.
5. FULL-PART ORDER: reuse existing PartSequencePlanner and BendVerifiedToolChangePlanner; only compare complete verified feasible paths. Rank by fewest tool change transitions, then fewest setups, then reorientations. Without all evidence report no verified order; do not fabricate best tool.

**Never confuse** native Check Min PASS with Check MAX PASS, source DXF loaded with physical clamp fit, a candidate collision with an entire-Part impossibility, or candidate CLEAR with production certificate.

## E. Deterministic result states (single conclusion)

- NOT_RUN: no current valid snapshot.
- RUNNING: no stale/cached CLEAR.
- MIN_NG: actual BelowMinimum (only the identified bend).
- EXCEL_LIMIT_FAIL: verified applicable case and verified exceeded numeric limit.
- CAD_COLLISION_CONFIRMED: real numeric collision with source-specific contact witness and pose.
- PARTIAL_CLEAR: one candidate/subtest clear but complete state/sequence missing.
- VERIFIED_FEASIBLE: ONLY one complete admissible tool/setup/sequence checked for the intended scope.
- UNKNOWN_CONFIG: missing/stale Excel or DXF configuration.
- UNKNOWN_COVERAGE: unsupported actual bent geometry or missing work states.
- UNKNOWN_MOUNT: tool curves exist but mounting/datum/work path not verified.

These are UI projections, not a license to change collision math. For 0 valid geometry show: CHƯA XÁC MINH CHẤN; Chưa nạp được thư viện dao; Check MAX/va chạm chưa kiểm tra. Do not suggest a tool if untested.

## F. End-to-end input recovery and performance fix

Actual code paths: Commands/BendToolProfiles/ToolProfileLibrary.cs (ToolProfileSourceSettings/Provider), ToolProfileLibrarySession.cs (2s Poll); Commands/BendPlacement/BendPlacementSources.cs (MasterPath/LimitWorkbookPath/LimitManifestPath/PairSeatingEvidencePath); UI/AddinSettingsControl.BendTools.cs (existing settings UI).

- Diagnose current settings path and override precedence from the actual process. Add one guided local source-selection workflow to existing UI (choose current valid manifest/config and read-only source folder, preview actual 18 pairs/hashes and missing mounting evidence), persist only after explicit user approval, atomically/backup-safely. NEVER guess file paths, silently copy stale settings or alter DXF/Excel.
- Show a single repairable source error. Avoid 2-second re-reads on known missing config: use controlled backoff, timestamp/fingerprint watcher or explicit refresh; keep existing VerifyForPublicationAsync freshness and cancel/dispose semantics. Do not cache/return an obsolete CLEAR after source changed.
- Fix repeated diagnostic errors by transition-only logging and exact message/stack when new InvalidDataException occurs, without changing exception semantics.
- UI update based on immutable same-run snapshot; part/bend/tool switching should NOT launch native checking again. Preserve correct selected Part/Bend after finishing Run and after Refresh. Ensure control reparenting, DPI/resize, Close/Cancel and error handling do not break.

## G. Exact protected areas and class paths

Modify existing class files DIRECTLY where required, especially:
UI/BendAssemblyBatchForm.UxV4.cs; UI/BendAssemblyBatchForm.UxV43.cs; UI/BendSection2DCanvas.cs; UI/BendAssemblyBatchModels.cs; UI/AddinSettingsControl.BendTools.cs; Commands/BendToolProfiles/ToolProfileLibrary.cs; Commands/BendToolProfiles/ToolProfileLibrarySession.cs; Commands/BendPlacement/BendLimitWorkbookReader.cs. Touch UI/BendAssemblyBatchController.cs, UI/BendAssemblyBatchLiveBackend.cs or existing planner only on an evidenced source-boundary defect. No separate V4.6 UI class. No pointless .csproj rewrites.

PROTECT: Check Min equations, CollisionEngine2D numeric kernel, 18 fixed Punch–Holder identities, original Tool CAD, native COM ownership/lifetime, source material, all other commands (Check Hole, BOM, AutoFrameJoint, etc.), existing Debug/x64 output path C:\Tao moi\ADDIN.dll.

## H. One complete acceptance checklist; one manual Rebuild afterwards

Before reporting ready (managed/static ONLY, not Build):
- Read and validate all real local source documents and hashes; no CAD/Excel file written.
- Scope/selection: entire vs selected, 2/6/10+ bends, unsupported bend, multi-config/duplicate Part.
- UI states: NOT_RUN, RUNNING, complete, canceled, stale, MIN_NG, Excel applicable, Excel unresolved, no Excel case, missing DXF, 18 loaded but 0 ClampVerified, proven collision; no false READY.
- Data reload: missing config currently, valid current config, changed fingerprint, failed manifest, valid 18 pair IDs; 38-repeat failure resolved without stale acceptance.
- Excel: exact 5 families/lengths and provenance; W/L not confused with machine support length; no screenshot-pixel limits; C/L/U/Z full-section matching and ambiguity.
- CAD: 18 original pairs, KIK open retained, no unaudited mounting; red witness comes from numeric result only.
- Result-first UI: large CAD section; selected Part/bend; one clear verdict; optional missing-data fix button; expandable 18 tools; no repeated technical noise or clipping on DPI/resize.
- Planner: verified complete sequence only; otherwise no invented fewest-switches recommendation.
- Static compile symbols/namespace/references and read-only tests PASS; project and COM stable; git diff limited to Check Chấn files.
- No 33-Part/120-component repeat. ONE small Part XL-02 plus ONE small Assembly reserved for the user's manual Debug after Codex finishes.

Report ONE comprehensive result with: file diff + commit SHA/PR URL, source problem cause+repair, Excel case applicability counts, 18 pair ready counts, immutable snapshot logic, each UI state test, what remains UNKNOWN because physical evidence is absent. Explicitly mark BUILD/REBUILD/NATIVE: NOT RUN. Do not claim manufacturing certification. Keep PR Draft and no merge.

## I. SINGLE Codex task

Implement ALL implementable A–H coherently in ONE source integration from the existing Draft PR #38, not small follow-on cosmetic fixes. Compare local worktree first and preserve existing local changes; do not reset/clean/stash/force checkout. Code directly in existing classes and update SAME PR #38 with a single reviewed set of commits. Do not auto Build/Rebuild or operate SOLIDWORKS. Do not fabricate Excel dimensions, tools, datum transforms, process states or approved sequence. If factual evidence is impossible to infer, implement the correct UNKNOWN and a single operator-facing setup/verification action, record the exact missing evidence once. Supply one comprehensive report then STOP. The user will manually Rebuild ONCE and provide screenshots/logs for the next acceptance.

This document is the authoritative replacement for the previous incremental Check Chấn UI prompts.
