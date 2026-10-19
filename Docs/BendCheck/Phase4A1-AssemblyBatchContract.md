# Phase 4A.1 — Assembly batch backend contract

Issue [#23](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/23), based on
Phase 4A `6717848b734a032b6317155556ab6183c8d54042`. This backend evaluates
member Parts against Punch and Upper Holder. It does not evaluate Assembly
installation interference, the lower die, order search or a winning tool.
`ManufacturingStatus` remains `CHUA_XAC_MINH`; `WinnerToolId` is always null.

## Native entry point and thread ownership

The existing `BendCheckSession` owns inventory, native captures and one
`AssemblyBatchCoordinator`. The minimum callable integration is:

```csharp
// On the existing SOLIDWORKS STA, after the session captures the selection:
var operation = session.BeginAssemblyBatch(
    selected ? AssemblyCheckScope.SelectedComponents : AssemblyCheckScope.EntireAssembly,
    minimumWorkbookSnapshot, new AssemblyBatchOptions(workers: 2), cancellationToken,
    progressCreatedOnSta, reviewedManagedProgramFactory);

// On successive STA message-loop turns. Never block STA waiting on Completion.
operation.Pump(maximumCaptures: 1);
// Await Completion without blocking the message loop. Read operation.Result
// only after the native/source freshness barrier finishes.
// While displaying results, keep polling CheckHostStamp / operation.Pump.
// operation.Cancel() immediately invalidates held results.
```

The optional factory is called on STA and returns immutable managed
`AssemblyProgramData` (envelopes, setup evidence, process plans, program-use
evidence and conditions revision). It must not embed COM objects in callbacks.
`Progress<T>` should be constructed on STA if the recipient updates controls;
a direct progress callback must perform managed work only.

No command, Designer or event handler is changed. The current button does
**not** automatically start this new backend. Antigravity can call this tested
entry point from the existing selected/all UI. Existing Check Min capture,
measurement, evaluation, highlighting and Planning logic are reused unchanged.

`Pump` yields between Parts. A single existing Min/COM capture is atomic and
can still block STA for its duration; this phase does not promise a 50 ms
maximum interaction latency or move any COM access onto a worker.

## Inventory and equivalence

- Selected roots expand recursively; overlapping selections are visited once.
  The original selection survives later face highlighting and Refresh.
- Occurrence identity binds the Assembly/configuration, parent chain, native
  IDs, referenced configuration and persistent reference when available.
  `Name2` alone is insufficient. Display paths, parent IDs, quantity and raw
  total transforms are retained. Presentation transforms are excluded.
- Transform metadata uses exact SOLIDWORKS `MathTransform.ArrayData`;
  translation is **metres**. Captured Part/envelope coordinates use **mm**.
  This transform is not a machine work pose. Scale, non-orthogonal, reflected
  or missing transforms cannot prove equivalence.
- Normal single sheet-metal bodies sharing a canonical source/configuration
  are captured once. The native update stamp, saved-source SHA when available,
  native body variant and capture lease participate in identity. Configuration
  mismatch or mixed source identities is explicit UNKNOWN.
- Assembly user bodies, unknown metadata, multibody, flexible ancestry, live
  external geometry references and mirrors are isolated/unsupported. Broken
  references are distinguished from locked, dangling or out-of-context links;
  the backend never breaks any reference. Virtual/unsaved sources are isolated.
- The existing Min file/config grouping and `Error` contract are retained.
  Additional Max/process restrictions live in `AssemblyBatchReason`, not in
  the Min error field. A source can therefore remain measurable by Check Min
  while Batch explicitly returns UNKNOWN for its unproven instance context.
- A mixed Min group is partitioned into eligible occurrences and independent
  blocked occurrences. The coordinator enforces this partition even for direct
  managed callers. Both recursive and directly selected descendants inspect
  native ancestor solving state; selecting a child cannot discard flexible
  ancestry. Known bend inventories retain 18 static/process slots on blocked
  occurrences, with no setup certificates transferred from normal siblings.
- The unconditional bulk lightweight resolve was removed. Lightweight and
  unresolved subassembly branches are explicit UNKNOWN diagnostics. The
  backend performs **zero** automatic resolves; it cannot certify exact editor,
  unloaded-document and reference-state rollback in all configurations. The
  documented action is to explicitly resolve the required components and rerun.
  The test fixture's setup/resolve probe is separate from the read-only backend.
- Alternate configurations use the existing protected working-copy API.
  Original configurations, files and dirty state are not changed by checking.

## Managed scheduling and caches

One coordinator dispatches FIFO analysis conditions across Parts using 1..4
compute workers total. The existing static/process workers run synchronously
with one worker inside a coordinator slot; there is no per-Part nested pool.
A session semaphore also bounds overlapping canceled/superseded generations.
File I/O tasks and the coordinator's waiting task are not numeric workers.
All SOLIDWORKS access stays in inventory/capture/freshness polling on STA.

Geometry reuse and collision-result reuse are separate. By default each
occurrence is a separate checking condition, even for the same local shape.
Sharing a result requires `AssemblyProgramUse.Reviewed`: external review of the
complete program binding and its independence from rigid Assembly placement.
The software-only `ControlledPartLocal` alternative permanently marks output
as simulation. Mirror, modified, flexible, virtual or unverified transforms
cannot use this invariant. Different actual setup/pose/axial ranges or process
revision cannot inherit an earlier result.

The caches are session scoped, single-flight and LRU bounded:

| Layer | Count limit | Default accounting budget | Binding |
| --- | ---: | ---: | --- |
| Geometry/envelope | 128 | 16 MiB | Source revision/config/body, exact managed capture, bend frames, envelope/observation bindings, compiler and mm policy |
| Static certificate | 4,096 | 16 MiB | Geometry, library/material/source, verified setup and exact object bindings, occurrence/program invariant, tolerances/errors |
| Process certificate | 4,096 | 32 MiB | Same, plus complete plan, segments/contact/constraints, simulation and review bindings |
| Catalog/preparation snapshot | 8 | 1 MiB accounting allowance | Exact immutable library snapshot, source/configuration/placement fingerprints |

Accounting bytes are conservative estimates for payloads, **not an exact CLR
retained-heap measurement**. Entry limits are hard bounds. Observed process
managed heap is reported separately. The existing source provider owns parsing
and shares its immutable 18-tool snapshot across all Parts; the catalog cache
does not parse DXF per occurrence.

Keys include object-lifetime bindings required by the existing setup API.
Observed-slice material/station/error also participates: two observations may
share a volume hash while proving different collisions. Equal setup coordinates
without equal verified evidence cannot hit a certificate. Reviewer/authority,
simulation and verified-plan identity cannot be replaced by payload equality.
The canonical externally reviewed program digest is kept separate from these
in-session object bindings. A factory rebuilding immutable evidence objects
gets conservative misses; it can retain unchanged DTOs for warm reuse.

Stored certificates contain detached status/reason/clearance/witness data.
Restore attaches the new run's guard and exact current inputs. An old canceled
generation's callbacks are never cached. Transient budget/cancel/stale/failure
results are not cached. All cache values are managed data; no Component2,
ModelDoc2, BendRow, minimum context or control enters a cache.

## Fast precheck, freshness and results

Without any supplied setup/process/envelope, the coordinator skips expensive
native-envelope compilation, supplies a source-bound UNKNOWN placeholder and
lets the existing workers retain exact missing-setup/process reasons with zero
numeric queries. With evidence, the existing conservative finite-axial and 2D
broadphase/narrowphase policies run unchanged. A bounding box never supplies a
manufacturing decision or an inferred tool reach/work pose.

Setup/plan indexes are immutable and preserve duplicates as UNKNOWN. Kernel
callbacks check cheap shared leases; the existing per-Part queries validate
their own plans/setups. Full cross-source checks run at dispatch, completion
and on held result access. This avoids rescanning all other Parts' full motion
graphs at each scalar kernel operation.

Native polling checks the host stamp/edit context each turn, audits bounded
groups of occurrence/Part state periodically, and performs a full audit before
starting/reusing and publishing a batch. File SHA observation is managed I/O,
before capture and publication, then periodically while displayed. The tool
provider performs an explicit end-of-batch revalidation. A changed disk source
cannot be rebound to an old open Part: that session requires source reload/new
session. Canceled/native-time-budget operations do not wait for unrelated file
reads before producing a non-current result. A single COM call cannot be
preempted; cancellation is cooperative at safe capture boundaries.

Native file-proof dependencies comprise eligible captured source files, the
host Assembly and the minimum workbook. An unsupported virtual occurrence's
temporary side file cannot invalidate unrelated normal occurrences; that
occurrence still returns UNKNOWN. Host/occurrence stamps, configuration, pose,
ancestor solving state and required-source/tool/review guards remain checked.

`AssemblyBatchSnapshot` retains component occurrences and source/configuration
counts separately from analysis jobs, capture hits/reads, compiler/precheck,
kernel/process timings, query counts, cache counters, observed heap and worker
counts. Times for multiple workers are accumulated CPU-stage wall durations and
can exceed whole-batch wall time. `PrecheckMilliseconds` measures coordinator
admissibility; it does not rename all engine preparation as narrowphase time.

Every known bend has all 18 candidates, independent Punch/Holder static results
and all required process pair/phase slots. Unsupported sources with an unknown
bend inventory retain a reason-coded occurrence, without inventing bend counts.
Unresolved branches mean the whole member inventory is not proven complete.
Progress is serialized, monotone and provisional; canceled/stale generations
are never complete current success. Stable output order is independent of
worker completion order. `GeometricStatus` aggregates candidate conflicts; it
is **not** a whole-Part feasibility or manufacturing verdict.

## API references used for native checks

- [GetModelDoc2](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.icomponent2~getmodeldoc2.html): document availability and instance-context limitations.
- [GetBodies3](https://help.solidworks.com/2018/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetBodies3.html): native instance/user-body metadata.
- [GetTotalTransform](https://help.solidworks.com/2018/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IComponent2~GetTotalTransform.html): total occurrence transform and presentation option.
- [External reference statuses](https://help.solidworks.com/2024/english/api/swconst/SOLIDWORKS.Interop.swconst~SOLIDWORKS.Interop.swconst.swExternalReferenceStatus_e.html): broken versus live/unresolved reference states.

Phase 4B still needs reviewed real setups/process geometry, all motion and
machine constraints, sequence search and the one-tool-for-whole-Part decision.
