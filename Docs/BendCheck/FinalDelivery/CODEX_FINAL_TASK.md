# CHECK CHẤN — FINAL DELIVERY / CODEX HANDOFF

Baseline: PR #35, commit 02cbe20fd8713734d1011ede22928f15025a87a8.
This is a **design, code-reference, and review handoff**, NOT a claim that production collision is solved.
Target integration: a separate Codex implementation Draft PR based on PR #35, not main.
Language/runtime: SOLIDWORKS Add-in, C# 7.3, .NET Framework 4.8, WinForms, x64, COM/STA.

## Business objective

The company physically has 18 punch sets; the supplied Master DXF includes 18 explicitly paired Punch–Holder CAD references. **Use those exact CAD pairs**, not fabricated replacement punches/holders and not the nearest neighbor on the drawing. The existing 33 native test Parts derived from 深曲げ限界.xlsx contain 72 real native bends. Do not generate another unrelated 33-case series.

The desired result is an auditable answer for each Part, bend, tool and Punch/Holder, and—only where proved—a checked-scope common-tool bend sequence. Prefer **one Punch–Holder combination with zero changes**. Never claim the Part is manufacturable merely because a test executable ran successfully.

## Verified software baseline (not manufacturing proof)

- PR #34: 18/18 fixed CAD pairs, 17 valid closed Punch contours, KIK-7181328001 open and unchanged. Actual CAD Holder contours may include SPLINE and reference entities; physical material roles/seating remain missing.
- PR #35: 33 saved SLDPRT / 72 native bends; every native bend carries 18 static + 18 process candidates. 69 Check Min bends meet minimum and 3 are below minimum (XL-19/20/21, native FeatureId 53, A=15 mm vs minimum=15.2 mm). This is correct NG recognition, not a failed test.
- PR #35: native real-company collision kernel queries=0 because working axial lengths, mounted coordinates, intermediate workpiece states and motion are not evidenced.
- Native SOLIDWORKS clean shutdown remains NOT_CONFIRMED / lifecycle FAIL. Functional tests do not override this.
- Reference lengths 4,200 / 6,000 mm from Excel are NOT certified effective tool lengths or machine capacities. W/L and along-bend length are independent.

## One integrated assignment (no serial micro-tasks)

Codex should research, implement, test, and publish ONE new Draft PR. The work can have internal commits/checkpoints but not repeated requests for a new user prompt.

### Workstream A — Holder material-review drafts for all 18 CAD pairs

1. Consume existing ToolProfileLibrarySnapshot.PlacementSources.Tools and the actual fixed catalog. No new DXF association algorithm, no mutation of original CAD.
2. For each ToolAssemblyProfile: enumerate every Holder.SourceEntities entry by HolderEntityGeometry.Key, source entity type, block INSERT lineage, layer, original spline data and bounded approximation error. Preserve unknown/unsupported source items.
3. Build an **unsigned, non-authoritative review draft** showing connected components / topology and which entities are not yet classified. Never auto-sign or auto-populate material/holes/reference from largest area or layer names.
4. Add a reviewer-driven way to explicitly assign each entity exactly once to Contours (Solid/Hole/Island with ParentId) or Excluded (Decorative/Centerline/Reference/Annotation). Existing HolderObstacleSet.Read must remain the only production material interpreter after independent review. Holder SPLINE conservatism and every boundary band remain mandatory.
5. Emit a per-tool status table: SOURCE_MATCHED / MATERIAL_ROLE_PENDING / MATERIAL_REVIEWED / SOURCE_STALE; give exact missing reasons. Open KIK remains rejected as valid Punch material until a separately approved source revision exists.
6. Do not commit source coordinates, customer CAD, private manifests, review keys, screenshots containing company geometry or signed private proofs.

Reference implementation for draft generation: Reference/HolderMaterialReviewDraft.cs (not wired into product until Codex reviews).

### Workstream B — setup/datum/axial evidence and simulation

1. Use ToolPairSeatingRecord, ToolDatumSet and ToolAxialSetupRecord; distinguish DXF_PRESENTATION_WCS_ONLY from an independently reviewed source-to-work frame and physical mounting datum. NEVER use root INSERT position as physical seating by default.
2. Require actual Punch and Holder source geometry, material roles, useful axial spans, setup error bounds, machine/process constraints, registration, review artifact and expiry.
3. Implement an explicit **software-only simulation input path** using existing CadReferenceSimulationRecord. A permitted simulation must be source-bound and independently authorized as SimulationOnly; it must remain tagged SIMULATION, and never set ClampVerified or CHUA_XAC_MINH to PASS.
4. If information is absent, deliver editable unsigned review *templates* with missing fields. Do not mint authority credentials, private signatures, or a physical inspection assertion.

### Workstream C — numeric collision + 33 native Parts + Phase 4B

1. Reuse HolderObstacleSet, ToolAxialSetupEvidence, AxialCollisionEngine, ProcessCollisionEngine, ProcessPackageImporter, ProcessProgramProvider and PartSequencePlanner. No replacement numeric solver.
2. Run independent native and numeric tests; pair the exact 18 CAD tools to the exact 72 observed native FeatureIds. Static and five-phase process tests are distinct. Reuse only correct source/configuration and reviewed setup; prove freshness on every published result.
3. For missing setup geometry, emit UNKNOWN and missing reasons, NEVER fabricated CLEAR/COLLISION. **A non-zero kernel query count in a synthetic fixture does not prove all 33 real Parts can be evaluated.**
4. Phase 4B only ranks complete CLEAR paths in the supplied, source-reviewed graph and its checked scope. It must not infer intermediate unbent states from the final model. One-tool/zero-change route has first preference. Multi-tool changeover search is a SEPARATE capability and cannot be simulated by UI sorting.
5. Record witnessed collision with native FeatureId, tool, Holder/Punch, process phase/segment, source hash, witness/error bounds. Do not label absence of a found route as proof no manufacturing sequence exists under bounded search.

### Workstream D — native lifetime root cause

1. Reproduce shutdown on an owned SOLIDWORKS PID, verify the **actually loaded in-process Add-in DLL binary SHA and path** match the tested build. Compare clean baseline / isolated add-in / new build with controlled fixture.
2. Attach private ProcDump/WinDbg diagnostics to owned process. Audit event subscriptions, WinForms timers, COM RCW lifetime, reentrant Dispose/Disconnect, delayed STA callbacks and test harness teardown. Never manually release borrowed host RCWs or kill the user's SOLIDWORKS instance.
3. Fix only demonstrated causes and rerun clean native teardown at the existing time budget; retain raw failures. A 0xc0000374 / RPC or nonzero exit remains FAIL/NOT_CONFIRMED even after numeric/assertion tests pass.

### Workstream E — UX and performance (do not redesign)

Keep the current single Check Chấn form and Check Min untouched. Show:
- 18 real ToolIds and fixed CAD Punch/Holder source identities for each selected bend;
- viewer for proposed/reviewed material roles and unsupported SPLINE/unknown paths;
- editable reviewed input only through an explicit, non-signing review flow;
- simulation vs reviewed geometry vs missing process evidence;
- Punch, Holder, static and per-phase process results separately;
- native FeatureIds, witnesses, source revocation and file provenance;
- no blank result masquerading as successful evaluation;
- progress/cancel/cold/warm; no extra modal per tool.

Antigravity may polish UI only after Codex's functional contracts are complete; protect backend, COM, event handlers and Check Min.

## Required delivery / final proposal

Open ONE Draft PR targeting feature/check-chan-native-excel-4200-6000, with:
1. Actual source changes, precise code paths, tests, and documented source contracts.
2. A proposed **final architecture choice** with alternatives, tradeoffs and why chosen.
3. 18-tool role review coverage; report supported/unsupported SPLINE and open KIK.
4. Per-case 33×18×bend results and honest NUMERIC_QUERIES, CLEAR/COLLISION/UNKNOWN, no inferences.
5. Case study XL-17 (6000 mm, W/L 50×100, AL3) with source and missing-evidence chain, not invented PASS.
6. Native shutdown A/B evidence with loaded DLL path/hash and private crash dump references, PASS/FAIL/NOT_RUN.
7. Release Debug/x64, Release/x64, managed regression, native Part/Assembly/33 case, caches, Cancel/Refresh/stale and independent oracles.
8. Exact human approvals/data needed, separating essential blockers from optional improvements.
9. Screenshots from actual Windows if available; otherwise NOT_RUN, not simulated-screen PASS.
10. No merge to main; do not delete earlier PRs or hide failures.

**Important:** This handoff supplies code references/architecture, not unsigned production certificates. Implement and test code; do not stop at a written plan. If critical physical evidence is absent, deliver the complete code path, truthful UNKNOWN, a user-review workflow, and a fact-based final proposal without inventing the missing evidence.
