# Phase 4B — common tool, sequence search and unified Check Chấn

Baseline: PR #29, `03eb514b6a32ff7a54ff2a7787bdd2e521cb4859`.
Delivery branch: `feature/check-chan-phase4b-unified`; draft targets
`feature/check-chan-unified-ui-polish`. No older PR or main is merged.

## What is actually computed

`Commands/BendPlanning/PartSequencePlanner.cs` searches an explicit, source-bound
process-state graph. It invokes the existing `ProcessCollisionEngine` for each
transition, motion segment and independent Punch/Upper Holder pair. The five
required phases are ApproachLoad, PreBend, DuringBend, FormingEnd and
RetractUnload. It does not introduce another numeric collision kernel or check
the lower V die.

Every Part result retains all 18 company ToolIds. Each path keeps one ToolId,
one setup identity and matching Punch/Holder source geometry/finite extents.
Inter-step tool poses must join. A path cannot exchange tools to combine
unrelated CLEAR results. A common tool exists only when at least one complete
supplied path for that tool has CLEAR results for every required pair/phase.
`CommonToolIds` is a list of evidence-bound alternatives; there is no automatic
manufacturing winner. `ManufacturingStatus` remains `CHUA_XAC_MINH`.

A current collision result prunes only its own branch, and only when it has a
matching process-payload witness, an actual source-volume interior margin,
positive axial occupancy and valid normalized motion parameter. An unwitnessed
COLLISION string cannot eliminate a route. Missing phases, unmatched endpoints,
ambiguous source identities and changed setups remain UNKNOWN.

## State and evidence contract

`SequenceSheetState` contains a state identity, the completed native FeatureIds
and an exact geometry/pose signature. `SequenceTransition` contains a native
FeatureId through its existing `BendProcessPlan`, before/after state identities
and a setup identity. `PartSequenceSpace` binds the graph to the exact managed
model, configuration, stamp, native geometry hash and persistent bend identities.
The state transition must add exactly its own, previously uncompleted bend.

Endpoint equality compares the complete supplied analytic panel sources,
registered frame, rigid poses, axial positions and uncertainty. It does not
compare a few slices. `SheetEnvelopeCapture.ShapeSha256` separates an analytic
shape/frame signature from the existing bend-specific capture signature; the
original `GeometrySha256` and collision mathematics are unchanged. Alternate
representations or different work frames are deliberately not inferred equal.
Supporting a reorientation requires an explicit registered process transition.
The native capture currently names a separate local frame for each bend.
Automatic cross-frame/native whole-sheet registration is **not implemented**
here. A multi-bend native graph cannot bypass the endpoint check by relabeling
those frames. Until a reviewed registered representation is available, such
joins remain `UNBOUND_SEQUENCE_STATE_GEOMETRY_OR_FRAME`, not CLEAR. The tested
multi-bend numeric fixtures explicitly supply a common frame and process law;
they do not establish this missing native registration capability.

The non-test entry is `PartSequenceSpace.Reviewed`. Its existing review verifier
checks purpose `PartSequenceSpace` against the entire payload, including source
revision, native geometry, graph, setup identities and the declaration of the
allowed alternatives. Coordinates and hash strings alone do not verify it.
`Controlled` is an internal simulation fixture entry. Native finished CAD is
never converted into a process-state graph by these factories.

`ExhaustiveDeclaredSpace` means exhaustive **only within the supplied reviewed
or controlled graph and conditions**. It is not a proof that every physical
manufacturing sequence has been considered. An incomplete graph, unsupported
registration or a resource cutoff cannot reject the whole Part. The result
distinguishes simulation, reviewed-source graph, missing evidence, bounded
search and interruption.

## Search, budgets and cache

Search is deterministic native-ID/transition-ID ordered depth-first traversal.
It pushes only reachable supplied successors; it never materializes n!
permutations. The default limits are 30 seconds, 5,000 visited nodes, 128 retained
path results, a 1,024-node frontier, 100,000 charged kernel calls and a 32 MiB
conservative allocation account. Policy limits have hard allowed maxima.
Graph input is capped at 100,000 states/transitions and indexed for validation.
The allocation account is an explicit conservative bound, not an OS peak-memory
measurement or a claim about total process memory, which also holds input DTOs.

The certificate cache is capped at 512 entries and at most 16 MiB of conservative
weights (also limited by the selected policy). Its key includes source/context,
model and graph object identities, complete graph revision/payload, tool library
and placement evidence fingerprints, transition payload and all numeric-policy
settings. Equal coordinates without equal bindings cannot share certificates.
Warm restoration rebinds numeric facts to the current run/source guard; old
cancellation callbacks are not retained as new proofs.

Full input audits run at search boundaries and publication, with shared lease
checks plus the active plan checks inside the existing kernel. Held result
getters still check complete freshness. Model, source-graph, process, setup,
library or review expiry withdraws common-tool claims. Async runs support
cancellation and supersession; superseded held proofs become UNKNOWN.
Progress is throttled to avoid flooding the UI message queue.

The planner detail view uses a synchronous private display-read scope with a
complete audit at entry and exit. Shared source leases remain checked during
the copy; an unrelated-plan invalidation at exit discards the entire display.
Retained getters and other threads remain strict, and exceptions unwind the
scope. This removes repeated whole-graph audits for every displayed cell,
without retaining a freshness decision across runs or creating a certificate.

## Product integration and COM boundary

`AssemblyProgramData.Sequence` is the optional reviewed-data-provider hook.
The Assembly coordinator preserves it through both native and managed
occurrence admission. Normal siblings still execute independently of mirrored,
flexible or assembly-cut siblings. A Part-local program must separately prove
equivalence before sharing a result. The legacy reviewed program hash is
unchanged when no sequence graph is supplied.

The current native Live provider has no reviewed company process-state graph.
It therefore exposes 18 explicit missing-evidence planning candidates; it does
not insert the controlled test plans into company Part results. Providing a
production graph importer/provider and physically reviewed process data is
still an integration requirement for production tool/sequence decisions.

`BendCheckSession.PartBatch.cs` supplies the real single-Part STA capture path
to the same managed Assembly coordinator. It captures once, preserves the
configuration and source Part, verifies workbook/tool/file freshness and reuses
the unchanged capture on a warm run. No temporary Assembly is manufactured.
Only managed DTOs and managed source leases cross to workers.

The existing Check Chấn task-pane button opens the unified Form for Part or
Assembly. Selected components remain the Assembly default when selected;
otherwise Entire is used. CAD highlighting resolves a fresh native persistent
Feature identity and uses an occurrence-set key, avoiding ambiguous SourceKeys
between normal and unsupported instances. The legacy Check Min form and Set
Bend Line implementation remain available in source. The new Check Min tab
projects their existing evaluator values/status/reasons from the same capture;
it does not recalculate or alter Min mathematics.

## UX and rendering

The Antigravity Designer is retained. The existing overview/scope/actions,
progress, search/filter and Part grid remain at the top. Functional detail tabs
in the same window are:

1. **Nếp & 18 dao:** native bend selection, 18 candidates, independent Static,
   Process, Punch/Holder, phase rows, source reasons and available witnesses.
2. **Dao & thứ tự:** all 18 whole-Part tool candidates, evidence/coverage filter,
   optional pinned navigation, retained sequences, native-ID timeline and the
   selected step's five phase pairs. Empty/missing evidence has a reason.
3. **Chẩn đoán:** native capture, worker, solver, planner, conversion, render and
   Run-to-visible timing; stale/cancel/source/backend errors.
4. **Check Min:** actual existing Min results and outer dimensions/limits,
   separate from Max/process/manufacturing evidence.

Only 250 Part rows are materialized per page; search covers the full projection.
Only one selected tool's sequences and one step's phase rows are rendered.
Part/bend/tool selection is preserved by source/occurrence identity when still
present, including its page. Long content has scrollbars/tooltips.
Controls removed during bend navigation are disposed; two owned fonts replace
unbounded per-selection font allocation. Preview, controlled SIMULATION and
Live are labeled separately. No popup loop or additional command is added.
Captured angle/radius and bend names are shown only when a unique native
metadata record exists; missing data remains explicit. A single-Part result
uses less vertical space for the overview table, leaving more room for detail.

The before/mid/after 2D image is explicitly a **rendering illustration** of the
supplied source law. Arc tessellation there is for display only; it is not used
as collision geometry, coverage evidence or a continuous-motion proof.

## Native stability diagnostics

`ADDIN_BEND_CAPTURE_DIAGNOSTIC_DIRECTORY` enables private, capped local phase
breadcrumbs and full exception stacks, including the previously lost
KeyNotFound capture stack. It changes no native API order, numerical tolerance,
timeout or Min exception/result semantics. Native harness reflection now
preserves original exception stacks with `ExceptionDispatchInfo`.

The validation report records failures separately from successful reruns,
distinguishes baseline inventory failure from planner execution, and keeps
company files/coordinates/raw logs/screenshots private. No source CAD/Excel,
review secret or production setup is committed.
