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

## J. ONE-SHOT INTEGRATION AUDIT — 2026-10-22

This is the **single consolidated engineering work order** after reviewing actual source at PR #38 HEAD `39f189f593017c979b9941bdac89da228a09691d`. It augments A–I, not a new UI phase or permission to claim unverified results. **Implement and review as one integrated patch, then one manual Debug/x64 acceptance. Do not keep creating cosmetic V4.x patches.**

### J1. Source-level blockers and dependency ordering

| Actual class / source observation | Required integrated correction | Negative acceptance (MUST remain UNKNOWN) |
|---|---|---|
| `ToolProfileLibrarySession.Poll`: new backoff/quick settings metadata stamp exists, but not the entire DXF/manifest/Excel/placement data fingerprint | Build one authoritative immutable effective-source identity per run: resolved settings + whitelisted overrides + DXF/manifest hash + Excel SHA + placement/review hash; invalidate *published result* on any source mismatch, explicit refresh or canceled generation. Keep expensive reads throttled; do not reuse old CLEAR. Cover config change *while* pending read/VerifyForPublicationAsync (including the completion race). | Same config file but DXF contents/Excel SHA changed; old CLEAR cannot survive. A missing source stays UNKNOWN even though 18 DEFAULT IDs are listed. |
| `ToolProfileSourceSettings.SaveApprovedSelection` and `UxV45RecoverSourceAsync` from commits `c0f1615e`/`39f189f5`: explicit preview/backed-up save is introduced, but the dialog can discard a previously chosen manifest on a No response, and 'no geometry for current bend' is not necessarily 'config missing' | Preserve all existing paths/mode by default. Edit each field only on explicit user change; offer a local preview of effective mode, paths, manifest hash and distinct catalog/geometry/mounting counts. Validate the chosen mode *before* atomic save; allow a safe retry of invalid JSON, backup old config, show override precedence, preserve KIK OPEN. Trigger recovery button only from actual source-health errors; not from an unrecognized bend or per-bend empty candidates. | No answer to a picker must not silently switch modes/delete manifest; 18 IDs ≠ 18 pairs loaded; invalid chosen config not saved as valid. |
| `BendLimitRuleDraft.FamilyMappingVerified => false`, `ReferenceOnly => true`; `BendLimitWorkbookReader.Read` unconditionally appends `UNVERIFIED_TOOL_FAMILY_MAPPING` | Add an **evidence-checked applicability adapter** on top of existing workbook reader and reviewed manifest, not blanket-false and not blanket-true. Tri-state for EACH Part/config/FeatureId/ToolId/case: MATCH_CONFIRMED, NOT_APPLICABLE_CONFIRMED, MATCH_UNRESOLVED. Require provenance, family→ToolId approval, material/thickness, actual measured W/L and bend length, machine system and pose, units & exact certified rule conditions. Unapproved illustration stays reference-only. Never reinterpret 4200/6000 as W/L max. | No approved mapping, ambiguous shape, cached formula/image dimensions, unknown pose or stale workbook -> UNRESOLVED and **no approved CAD fallback**. Negative match only when all required conditions have been evaluated. |
| `BendStateDraft2D.SequenceVerified => false`; `ToolCandidateDraft.ReadyForCollisionCheck => false`; `ToolCandidateDraft.GeometryCheckReady` is simulation-only; `CollisionWorkPoseEvidence.Approved` enforces physical review | Separate **reference/simulation** and **verified production** readiness with evidence-bearing immutable state and no inferred promotion. Do not relax physical clamp, holder obstacle, work-pose, before/during/after state or workframe gates to make a green UI. Reuse existing collision kernel untouched. | 18 CAD pairs loaded with ClampVerified=0, missing holder seating, unknown pre/mid/post states, KIK open punch -> UNKNOWN (not CLEAR). Simulation collision is labelled candidate-only. |
| `BatchViewModelConverter.PartsFromRead` obtains `featureIds` from static/process candidate arrays; this can conceal a known bend when evidence is absent | Build stable native bend inventory from recognized BendMetadata/config/FeatureId first; treat tool candidates as **optional evidence**. Preserve duplicate Part occurrences and config identities; differentiate verified zero bends vs unsupported recognition vs canceled/stale. Keep B1…Bn even with 0 tool geometry. | Unsupported with no candidate is UNRECOGNIZED, not '0 bends'; six recognized bends remain B1…B6 with zero numeric queries. |
| `BatchViewModelConverter` uses `Any(COLLISION)` over candidate tools to set bend/Part collision status | Keep candidate/tool/phase outcomes separate from whole-bend feasibility and full-Part verdict. A witnessed candidate collision does **not** mean no feasible tool. Whole-Part success needs one complete fully verified path; no path found inside incomplete evidence is UNKNOWN. | One Punch candidate collides while another candidate is untested -> no 'Part cannot bend' or global FAIL. |
| `UxV43UpdateSection` draws current final-Part section and stores *unregistered* raw Punch/Holder; `BendSection2DScene` has contact/placement fields but does not yet consume verified current witnesses | Add one typed same-snapshot Part/config/FeatureId/ToolId/state-coordinate adapter from actual numerical `AxialCollisionWitness` / `ProcessCollisionWitness` to registered canvas curves/segment IDs. Paint red *only* verified actual Part and Punch/Holder contact portions. Final folded section must be labelled as final, never passed off as before/during geometry. No nearest-line guessing or hypothetical red zones. | Missing registration, multiple/conflicting envelopes, different source revision, witness absent or out-of-frame -> Part-only section with concise UNKNOWN, zero red contact strokes. |
| `BendVerifiedToolChangePlanner.TryChoose` already accepts certified transitions and optimizes reachable complete paths, but upstream verified state transitions can be absent | Supply transitions only from a complete verified process graph; preserve the search limit and current-source revision. Prioritize complete feasible path, then minimum tool changes, setup changes, reorientations; preserve previously folded flange constraints. Keep existing planner rather than rewriting. | No approved states/graph -> 'Chưa xác minh thứ tự chấn' with no recommended tool or invented sequence. |
| Current `UxV45UpdateOutcome` sets MAX always 'chưa đối chiếu', counts geometries from current bend candidates and can show candidate collision without numeric witness/verification | Introduce a **single deterministic typed result projection**: NOT_RUN, RUNNING, MIN_NG, EXCEL_LIMIT_FAIL, CAD_COLLISION_CONFIRMED, PARTIAL_CLEAR, VERIFIED_FEASIBLE, UNKNOWN_CONFIG, UNKNOWN_COVERAGE, UNKNOWN_MOUNT; include scope (candidate/bend/Part), evidence, fingerprint and reason. Render one operator verdict and Min/ExcelMAX/Collision/Source detail, with 18 tools tucked into existing tab. Reuse current controls; no `.UxV46.cs`, no extra CommandBar or source rewrite. | Missing Excel match or missing clamp never produces Check MAX PASS/production CLEAR. A candidate CLEAR remains partial. No repeated 18 UNKNOWN rows on primary UI. |

### J2. Engineering sequence for ONE integration (dependency graph, NOT separate releases)

1. **Preflight once:** read-only compare actual *local* `C:\Tao moi\ADDIN` worktree against PR #38 HEAD. Note local changes, old runtime configurations, original Excel/CAD/CSV and hashes; do not reset, clean, stash, force checkout, upload company files or change active build output. First decide how commits `c0f1615e`/`39f189f5` interact with actual original local source.
2. **Model contracts in existing Check Chấn class files:** define immutable run/source identity, per-feature inventory and typed tri-state Excel/collision/sequence/evidence result. Separate effective source health and candidate availability; no new parallel UI version class.
3. **Implement data boundary and approved Excel rules together:** config recovery + fingerprints/invalidation + workbook numerical/provenance matcher + correct native FeatureId inventory. Early fail is explicit UNKNOWN with exact missing cause.
4. **Implement physical evidence conversion and planner together:** independently verify actual Punch/Holder mounting/holder obstacles, valid intermediate work states, collision witness coordinate frame, and certified full process transitions. Do NOT weaken existing collision/kernel/COM ownership/Check Min or rewrite external original geometry.
5. **Bind existing WinForms UI last:** one snapshot powers list, large canvas, Min/MAX/Collision and conditional source recovery; selection only rebinds, does not rerun native COM. Show red only from witnessed actual contact and current Part/Tool identity; test resize/DPI, cancel/refresh/dispose and strict snapshot invalidation.
6. **Review as ONE source integration:** inspect changed-file list, C# references/namespace/brace/conditional paths, source gate precedence, UTF-8 text, `git diff --check` and non-build source inspections. Add focused managed test cases to existing tests but **DO NOT RUN** compiler/dotnet build/msbuild/csc/native; the user will do one Debug/x64 once after source review. Commit/push same branch/PR #38 ONLY after completed feasible changes are internally coherent; keep Draft, no merge.

### J3. Mandatory acceptance matrix (source/static now; native only after user-controlled rebuild)

- A: Valid 18 source identities; both observed real configurations are possible across different sessions: missing-settings 18 IDs/0 geometry and earlier native 18 DataCount/18 MatchedPairs/0 ClampVerified. Neither is proof of physical fit. Source files change with same settings path, changed workbook SHA, canceled read, failed review/manifest, recovery/rollback: no stale CLEAR, one meaningful error.
- B: Native inventory 2, 6, 10+ bends; C/L/U/Z incl opposite flange and different bend direction; unsupported/ambiguous 2D section; duplicate Part name with different config; selected Assembly occurrences; B1…Bn stable under no candidate or failed source.
- C: Excel approved applicable/inapplicable/ambiguous with material, machine, W/L, bend length, units, workbook SHA and cell provenance. 4200/6000 **bend-line support lengths only**, never flange height; source drawings and pixel distances always reference-only until separately certified. No 'no case' inferred merely from absent mapping.
- D: Missing/partial/unreviewed CAD Punch/Holder, unplaced holder, KIK deliberately OPEN, registered approved pose, pre/mid/post states, collision witness by both Punch and Holder. No overlay and no red without a current numeric witness. A static CLEAR is not a manufacturing certificate.
- E: Verified full feasible paths (minimize tool changes then setups/reorientations); missing one phase, missing transitions, incomplete search, canceled or stale graphs -> no optimal recommendation.
- F: Main UI one verdict, correct scope, Part 2D cross-section, exact source/config/feature/tool linkage; separate detailed tab; Check Min parity (including FeatureIds 524/530); correct Run/Refresh/Cancel/Close and DPI.
- G: Static source check and changed files report with actual evidence counts, source SHA/PR commit link; untouched non-Check-Chấn commands and protected assets; **BUILD/REBUILD/NATIVE NOT RUN**, no DLL/PDB delivery.
- H: After source integration, user performs **one** manual Debug/x64 and bounded XL-02 + one small Assembly test, then sends screenshots/logs. If source evidence is unavailable, state exactly what is blocked instead of inventing production verification.

### J4. Finish criteria and reporting rule

**One comprehensive handoff only, not a series of 'fix this one UI bug' suggestions.** Report (a) exact modified existing classes and commit, (b) whether source recovery truly reads local source, (c) Excel applicability counts and missing approved mapping, (d) 18 catalog/ready-geometry/approved-mount/numeric counts separately, (e) candidate vs bend vs Part outcome scopes, (f) contact-witness sources or why red is absent, (g) checked complete path or exact missing transitions, (h) static checks performed and native checks NOT RUN. Never claim final verified feasibility with absent physical evidence. Stop after one integrated report.
