# Assembly Batch UI integration

The integration branch starts at Antigravity UI commit
`1778427bfeaf215d6f58168dd3fadf0944fb9a39`. Its ancestry already contains
Phase 4A.1 backend `d185fea433b6c0e2fd03f738119319c0207e8b17` (PR #24).
There is no backend merge or engine change in this integration.

## Live binding

- Native FeatureIds come from the static/process candidate inventories. Ordinal
  BendIndex is presentation only. A zero-bend result has no bend rows. A missing
  inventory is explained rather than repaired with fabricated IDs.
- Every known bend retains the complete 18-tool display inventory. Missing or
  ambiguous candidates are UNKNOWN. The current backend snapshot has no native
  angle/radius DTO, so both dimensions display **Chưa có dữ liệu**.
- Static Punch/Holder results and process results are separate. Every
  `ProcessPairResult` retains its exact phase, pair and segment ID. Phase CLEAR
  requires both pairs for matching, nonduplicated segment IDs; UNKNOWN or missing
  required phases cannot become full-path CLEAR. Hover the motion cell for all
  segment/pair results and reasons.
- A Part's geometric conflict label means a candidate has a conflict. It is not
  a whole-Part feasibility, tool-selection or manufacturing decision.
  Manufacturing remains **CHƯA XÁC MINH**.
- Invalid snapshots are withdrawn. A held snapshot is polled through Pump after
  completion, including native pose, configuration and file-source freshness.

## Thread and operation ownership

`BendAssemblyBatchLiveBackend` owns an independent `BendCheckSession` and the
real minimum-workbook provider. `BendAssemblyBatchController` starts
`BeginAssemblyBatch`, pumps one native capture per WinForms timer turn, and
awaits `Completion`. A private STA dispatch handle explicitly returns I/O and
completion continuations to the originating thread; this also works when native
COM/message pumping has replaced the ambient SynchronizationContext.

Only managed workbook I/O and the existing managed workers run off STA. There
is no synchronous Task wait in the integration. An individual native capture is
still atomic and can occupy STA for its duration, as documented by Phase 4A.1.

The legacy Check Min window pauses its scan timer while the owned Batch window
is open and resumes its existing freshness checks afterward. Its measurement,
math, evaluation and context contracts are unchanged. The Designer is unchanged.
The GridStyler opt-out is local to Batch; existing callers keep BOM fitting.

## User actions

| Action | Behavior |
| --- | --- |
| Open | Empty LIVE view; no sample data |
| Preview | Explicit sample view, labeled as non-CAD data |
| Run | Withdraw previous/sample results, read current workbook, then begin live work |
| Selected Components | Capture the current native selection; clicking it again captures a new selection |
| Entire Assembly | Use the backend's entire Assembly scope |
| Refresh | Invalidate and rerun the saved scope, retaining captured selection |
| Cancel | Request cancellation, retain running state until backend acknowledgment, publish no canceled rows |
| Stale | Withdraw rows and request a new run after the model is active and rebuilt |
| Close during run | Cancel, defer closing/disposal until Pump returns and the asynchronous operation finishes |

Provisional progress is capped at 99%. Only current backend completion with a
complete inventory and no budget/cancel/stale interruption can display 100%.
This is completion of the audit, including possible UNKNOWN results; it does
not certify production. Total elapsed, native capture and worker times are
reported separately. Unavailable stage times are labeled as missing data.

## Reproducible checks

Build `ADDIN.csproj` as Debug and Release x64 with private OutputPath and
IntermediateOutputPath. Disable PreBuildEvent, PostBuildEvent,
KillSolidWorksOnBuild and RegisterForComInterop for isolated validation.

- `Tests/BendToolProfiles/BendAssemblyUi.Tests.csproj`: actual Form and converter
  regression, including asynchronous source reads without an ambient context,
  nonconsecutive FeatureIds, zero bends, missing metadata, independent phases,
  UNKNOWN, COLLISION, invalidation, rejection, cancel, refresh and reentrant close.
- `ToolProfiles.Tests.csproj`: unchanged geometry/collision/process/Assembly
  regression; `BendPlanning.Tests.csproj`: existing Planning regression.
- `NativeAssemblyBatch.Validation.exe --ui <dll> <new-private-output>
  <six-bend-seed> <assembly-template> <minimum-workbook> <cut-plane-seed>`:
  real Form/adapter/coordinator, generated zero/three-bend Parts plus a private
  copy of a six-bend Part, cold/warm runs, empty/selected/entire scopes,
  cancel/restart, native pose staleness and close. The existing edge harness
  verifies normal/reflected/flexible/Assembly-cut isolation before testing the
  live Form on that mixed Assembly. Seeds are copied and their original hashes
  are checked. The cut-plane seed must intersect the edge harness's template
  plane; an unsuccessful native cut is a failed fixture, not a backend PASS.

The `--render <private-output>` option on the UI tests renders the actual Form
at 1.0, 1.25 and 1.5 scale. This is a layout probe, **not** certification of
Windows display settings at 125%/150% DPI. Native CAD files, workbooks, source
hashes, private reports and screenshots stay outside Git.

## Antigravity layout handoff

The preserved layout has title/bend-pill overlap and truncated scope/Preview,
occurrence and tool-grid text at the tested sizes. Full values remain available
in tooltips and diagnostic text. Antigravity should resolve these on the UI
branch, then validate actual 100%, 125% and 150% Windows DPI (including monitor
changes). This integration does not claim those outstanding layout checks pass.
