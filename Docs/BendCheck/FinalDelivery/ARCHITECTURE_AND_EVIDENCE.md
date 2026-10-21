# CHECK CHẤN — source-aligned final architecture and evidence gates

Based on actual code at PR #35. The two C# sources under Reference/ are **integration candidates** / examples; they are not currently compiled or executed by ADDIN.csproj.

## 0. Non-negotiable distinctions

| Signal | Means | Does not mean |
| --- | --- | --- |
| CadPairBinding.Matched | The authored Punch/Holder INSERTs belong to the fixed drawing cell | Physical mounting approved |
| Punch.ObservedClosed + Region.IsValidRegion | Punch boundary is a suitable analytic region | Physical offset, machine setup or axial length verified |
| HolderObstacleSet.Topology | Observed graph components and open/closed candidates | Automatic solid/hole/reference classification |
| HolderObstacleSet.GeometrySupportedForCollision | Explicit reviewed material roles and conservative geometry | Holder physically installed correctly |
| ToolPairSeatingEvidence.CoordinateRecordReady | Reviewed coordinate/data relationships | PhysicalInspectionAccepted |
| ToolPairSeatingEvidence.PhysicalInspectionAccepted | Independently checked physical seating with proper authority | Approved complete bend process |
| ToolAxialSetupEvidence + reviewed source | Position, axial span/errors and native bend binding | Automatically reviewed intermediate sheet/motion |
| CadReferenceSimulation.Read | Audited source-bound software-only tool geometry | Manufacturing capability |
| PartSequencePlanner complete CLEAR | Complete CLEAR path within a supplied checked graph | Every physical route or machine capability |
| Native functional PASS | Product code path runs, dimensions/identity/Min match | Clean native lifecycle or physical collision result |

All results must preserve: tool ID, SHA256(s), snapshot/configuration, FeatureId/PersistentId, setup revision, evidence/reviewer status, expiry, numeric policy, cancellation/stale state.

## 1. Source-code map — use these exact existing classes

- Commands/BendPlacement/CompanyCadPairs.cs: CadPairBinding.Validate; exact source block/root/layer/presentation frame validation.
- Commands/BendPlacement/BendMasterAssemblyReader.cs: master manifest V2, two independent geometry selections. The 18 associations are already solved. Do not rewrite pairing.
- Commands/BendToolProfiles/ToolDxfReader.cs: original LINE/ARC/POLYLINE/SPLINE nested INSERT geometry and source transforms.
- Commands/BendPlacement/HolderObstacleGeometry.cs: HolderEntityGeometry; HolderTopologyInventory; HolderContourDefinition; HolderExcludedEntity; HolderMaterialDefinition; HolderObstacleSet.Read, Binding and Payload.
- Commands/BendPlacement/ToolHolderEvidenceReader.cs: HolderEvidenceDocument.Materials / Mappings, ToolHolderMappingRecord and verified source reading.
- Commands/BendPlacement/ToolPairSeatingEvidence.cs: ToolPairSeatingRecord with HolderSourceToPunchSource, tip/mount datums and physical evidence.
- Commands/BendEnvelope/ToolAxialSetupEvidence.cs: ToolAxialSetupRecord (FeatureId/pair/finite axial segments/uncertainty).
- Commands/BendPlanning/CadReferenceSimulation.cs: optional explicit simulation geometry of the **actual CAD pair**, signed under purpose CadReferenceSimulation, SimulationOnly=true.
- Commands/BendPlanning/ProcessPackageImporter.cs and ProcessProgramProvider.cs: independently reviewed full-sheet geometry and per-transition source evidence.
- Commands/BendPlanning/PartSequencePlanner.cs: existing common-tool sequence search, bounded/cancelled/freshness.
- Commands/BendProcess/ProcessCollisionEngine.cs: existing continuous five-phase collision kernel.
- Tests/BendToolProfiles/NativeExcelMatrixValidation.cs: actual 33 Part B-rep, 72 bends, 18 candidates per bend, 0 numeric company queries so far.
- Tests/BendToolProfiles/NativeTestProcess.cs: owned SOLIDWORKS PID and private diagnostic instrumentation.

## 2. Holder classification workflow (read-only first)

For each of the 18 ToolAssemblyProfile objects:

1. Validate tool.CadPair?.Matched, the exact fixed SourceSha/ManifestSha and current snapshot. Invalid/changed catalog => no classification promotion.
2. Enumerate **all** Holder.SourceEntities through HolderEntityGeometry(e, token); retain EntityKey = InstancePath|SourceRecord|Handle, Layer, SourceType, original NURBS and approximation error. A SOURCE_SPLINE with bounded chords is NOT automatically a material boundary.
3. Output topology-only metadata from HolderTopologyInventory (connected, closed, open/branched). It is NOT a material classification.
4. Reviewer explicitly assigns entity keys to:
   - Contours: Solid / Hole / Island, with ParentId and component hierarchy.
   - Excluded: Decorative / Centerline / Reference / Annotation.
   Every source key must appear exactly once. A reviewed composition may use source entities to form a contour without modifying the source.
5. The draft must be unsigned and simulation/pending until independently reviewed. Call HolderObstacleSet.Payload(definition) to obtain canonical digest; external review authority must sign it. Production code uses HolderObstacleSet.Read, not a custom unchecked fill.
6. For SPLINE, preserve all approximation uncertainty bands and check no blanket tolerance reduction; if unsupported -> reason, never silently drop.
7. Only a separately valid ToolHolderMappingRecord, ToolPairSeatingEvidence and ToolAxialSetupEvidence can raise setup readiness.

**Do not** auto-select the outermost region, discard holes/unknowns, auto-close KIK, relabel DXF reference lines as solid, invent tip coordinates, silently ignore unsupported entities or self-sign any review.

## 3. Mounting and axial evidence: user-facing review

The CAD's drawing-box WCS is a presentation coordinate system, not machine datum. Build an explicit table by ToolId:
- Pair identity: PunchRootInsert, HolderRootInsert, SourceSha, ManifestSha.
- Holder material roles: Reviewed / Missing / Stale / Unsupported / SimulationOnly.
- Real mounting: tip datum, HolderSourceToPunchSource, mount datum, seating confirmation, contact frame.
- Axial source: useful finite Punch and Holder segment ranges, tool-axis direction, errors, mirror allowance, reviewed native FeatureId.
- Process: before/after registered full-sheet states, 5 phases, restrictions and forming contact.
- Provenance/expiry: exact source, independent approval, missing reason(s).

A reviewer can edit an **unsigned proposal** and preview placement without touching original CAD. The application must not generate signatures or mark PhysicalInspectionConfirmed based on drawing position. Only approved records flow into existing verifier.

## 4. Readiness and numeric result policy

| Inputs | Permitted action | Label |
| --- | --- | --- |
| No matched fixed pair | Inventory/missing reason only | UNKNOWN |
| Matched pair, no material role or no finite mounting | Render CAD reference + review draft | UNKNOWN |
| Source-bound, reviewed simulation pose + geometry | Compute static/phase simulations within explicit input scope | SIMULATION CLEAR/COLLISION/UNKNOWN |
| Reviewed physical setup + axial coverage but no reviewed process graph | Compute supported static claims; process remains UNKNOWN | STATIC ONLY |
| Reviewed process graph + states + motion + setup + verified geometry | Run numeric process and checked-scope planner | CHECKED SCOPE CLEAR/COLLISION/UNKNOWN |
| Stale, cancelled, budget-exceeded, incomplete path | Discard/withdraw proof; do not promote | UNKNOWN |
| Numeric CLEAR and native functional PASS, no independent manufacturing authorization | No production approval | CHUA_XAC_MINH |

Collision must retain distinct Punch and Holder checks, phase (ApproachLoad, PreBend, DuringBend, FormingEnd, RetractUnload), segment, witness and error. A witnessed COLLISION for one ToolId does NOT prove all 18 tools fail. Absence of a full CLEAR path under bounded search is NOT proof none exist. If production requirements demand V-die or other machine constraints, mark those outside the current scope.

## 5. Data contract for a reviewer-operated preview UI

Minimally display the following managed-only fields (never COM objects on workers):
ToolId, CadSlotId, MasterSha256, ManifestSha256, HolderRootInsert, EntityKey,
SourceType, Layer, SourceClosed, Supported, ApproximationErrorMm,
ProposedRole (always NeedsReview on initial draft), ReviewedRole, ReviewPurpose,
AuthorityId, ReviewStatus, FrameId, SetupStatus, AxialStatus, ProcessStatus,
SimulationOnly, Current, Diagnostics.

Tab to use: existing Check Chấn -> Nếp & 18 dao / Dao & thứ tự / Chẩn đoán.
Add a preview panel, not a new command. The preview may highlight geometry; no false colors or PASS when role is merely guessed.

## 6. Solver and lifecycle boundaries

- Keep solver mathematical/numeric policy, Check Min math, and source CAD/Excel immutable.
- COM only on STA; no IComponent2, ModelDoc2, Sketch or COM proxies in Task.Run or worker DTO.
- Static/phase result and cache keys must bind source, model/config/update stamp, reviewed tool setup, process revision and numeric policy. Stale source withdraws all held results, even if a prior run said CLEAR.
- Native test harness must verify loaded Add-in DLL path and SHA, owned PID identity and clean ExitApp, not merely UI pass. Separate fixture/environment failures from product crashes with captured A/B evidence.
- Do not claim an unimplemented V-die inspection or multi-tool changeover planner.
- For a revised UI, Codex handles functional integration; Antigravity edits Designer visuals only, protecting backend/event handlers.

## 7. Decision required from Codex

Compare at least two viable architectures:
A. **Reviewed external evidence adapter** into HolderObstacleSet/ToolPairSeatingEvidence/ProcessProgramProvider (preferred initial hypothesis).
B. CAD viewport + source-bound simulation geometry without production certification.
C. Optional more general native registration/process capture extension, only if safe and evidence is available.

For each alternative provide: achievable scope with existing 18 CAD pairs, necessary human evidence, source-code footprint, proof/freshness model, estimated performance, numerical oracle and native stability risk. Explain the chosen architecture based on actual code and measured results, not optimistic claims.
