# Phase 4A.2 — process performance and proof preservation

Baseline: PR #24, `d185fea433b6c0e2fd03f738119319c0207e8b17`.
Branch: `feature/check-chan-phase4a2-performance-stability`.

## Measured bottleneck

The 18-tool/six-bend benchmark supplies 108 plans, five phases and two tool
pairs. The old process-worker invalidation callback scanned every plan inside
each low-level collision guard, and often evaluated the same request twice.
The instrumented baseline observed 1,133,640 full-request checks and 123,553,584
plan checks: 80,162 ms in full freshness checks, including 74,006 ms in plan
checks. These are nested inclusive times, not additive CPU accounting.
The source motion-material, envelope and tool-pose construction stages cost
834 / 890 / 658 ms. Interval explosion was not the primary cause.

The original 120-second job budget failed in prior validation. A baseline
rerun on this machine completed in 85.35 s independent / 87.06 s cold with
13,572 queries. This variability is retained; a timeout is not a geometric
answer. The production 120-second job budget, per-pair budget, subdivision
depth and numeric tolerances are unchanged.

## Changes and safety argument

1. **Guard locality.** Every query still checks cancellation, job limits,
   Assembly shared leases, model/library/observation leases and the active
   plan's full dynamic geometry/tool/review guards. The complete request graph
   is audited before and after candidate work and at publication. Held
   snapshots continue to run the original full request audit on access.
   Invalidating an unrelated plan during a numeric query cannot publish a
   partial success; the new regression triggers exactly that case.
2. **Parent-interval certificates.** A per-evaluation 32-bit mask records only
   parts proven CLEAR by the existing axial engine over a complete parent
   interval. Its children are subsets of that interval and reuse that proof.
   Endpoint samples, intersecting enclosures, UNKNOWN and contact exemptions
   cannot set a bit. Plan validation limits a segment to 32 parts. The mask
   never crosses segment, pair, source, setup, worker or cache boundaries.
3. **Stationary occupancy.** A certified actual-state separation is reusable
   over a whole interval only when both declared motion laws are exactly
   constant (angle, translation and axial position). All original source and
   numeric error bands remain. Non-exact material cannot enter the actual
   collision-witness path. Enclosure-only and uncertain-axial parts without
   an actual-state certificate still use the original sweep query.
4. **Immutable work.** Motion-law validation/identity hashes and tool-source
   identity hashes are reused from immutable DTOs. Live `Current`, review,
   physical setup, source-file and geometry leases are never cached as true.
   Source LINE/ARC/SPLINE geometry and uncertainty are unchanged.

Process policy metadata is V2, so Assembly certificate keys distinguish the
implementation. There is no new manufacturing verdict or tool winner. No lower
die, Check Min math, UI/Designer/event handler or command is changed.

## Diagnostics

`ProcessPerformanceScope` is optional and thread local. `WithPerformance` on
process options and `WithProcessPerformance` on Assembly options accept managed
observers only. Samples identify tool/feature, phase and Punch/Holder pair;
they include inclusive timing and counts for freshness, motion construction,
axial queries, intervals/refinements and certified proof reuse. An observer
exception cannot corrupt a result. Diagnostics do not enter evidence or keys.

Process snapshots also report full audits, shared-guard checks and full-audit
milliseconds. Assembly aggregates these only for actual process work: a warm
certificate cache hit does not fabricate fresh kernel/audit work. Existing
worker limits, FIFO jobs, session semaphore and cancellation remain in place.

Run `ToolProfiles.Tests.exe --assembly-benchmark <private-output>` for all three
fixtures; its optional third argument selects `Numeric18x3`, `Numeric18x6` or
`Numeric120x25`. Set `ADDIN_PROCESS_PROFILE=1` for phase/pair stage reports.
Raw profile fixtures are controlled synthetic geometry, not company approvals.
Fingerprint comparison/validation overhead is measured separately from solver
wall time; live held-result freshness is deliberately preserved during reads.

`--process-reference <private-output>` exports complete synthetic three/six-bend
pair semantics directly. Before/after exports compare status, reasons, witness
parameter/coordinates/material IDs/interior margins, tolerance and expected
contact data. Direct pair evaluation separates mathematical equivalence from
a whole batch invalidated by its wall-time budget; it does not extend a budget.

## Native STA response

COM remains entirely on STA. Worker inputs and caches contain managed DTOs.
Native capture is measured separately from process computation. The existing
single-Part capture is atomic: yielding between Parts cannot bound the duration
of a single capture. Warm capture reuse avoids repeated reads.

A future responsiveness change can make the native topology snapshotter yield
between bounded face/edge batches on STA, validating host/configuration/stamps
before and after every batch. Publication must wait for a complete snapshot;
edits/cancellation must discard unfinished captures. This requires separate
native capture regression and does not justify moving COM to a worker or using
partial topology as coverage. It is a proposal, not a completed change here.

## Native fixture stability

Two fresh-fixture construction attempts failed in native SOLIDWORKS before the
batch engine was called; Windows recorded heap corruption `0xc0000374`.
No claim is made that this identifies the underlying SOLIDWORKS defect.
The test harness can clone the previously verified private fixture via
`ADDIN_NATIVE_FIXTURE_SOURCE`: it copies 23 Parts/three Assemblies into a new
output, relocates closed-document references, rejects every reference outside
that output before any fixture save, and checks all source fixture hashes.
It does not use a theoretical DTO substitute for native Assembly testing.
No CAD/Excel/STEP/private evidence or raw logs belong in the public repository.

Observed results and remaining failures are in
[Phase 4A.2 validation](Validation-Phase4A2.md).
