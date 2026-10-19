# Phase 4A.2 validation — 2026-10-19

Baseline: PR #24, `d185fea433b6c0e2fd03f738119319c0207e8b17`.
Branch: `feature/check-chan-phase4a2-performance-stability`.
Draft target: `feature/check-chan-phase4a1-assembly-batch`. No merge.

## Implementation and root cause

The old process guard rescanned the whole 108-plan graph inside low-level
collision guards. Instrumented six-bend baseline: 1,133,640 full-request
checks / 123,553,584 plan checks, 80,162 ms inclusive freshness time.
The repair keeps active-plan/shared guards per query, complete graph audits
at candidate/publication boundaries, and full held-result freshness checks.
It also reuses certified parent-interval and stationary-occupancy separation,
and immutable law/tool identity work. No sampled-state shortcut is introduced.

The budget is still 120,000 ms per process job; pair limits, tolerances and
subdivision depth are unchanged. The native capture path/Min math, UI,
Designer, event handlers and other commands are unchanged. Policy metadata V2
distinguishes certificate keys. Manufacturing remains `CHUA_XAC_MINH`.

Proof details and reproduction are in
[Process performance contract](Phase4A2-ProcessPerformance.md).

## Observed regression

| Check | Actual result |
| --- | --- |
| Product Debug/x64 and Release/x64 | PASS, 0 errors, 16 inherited warnings each |
| Managed Debug/Release x64 | 263 PASS / 0 FAIL, 8,839 assertions each |
| Assembly-only Debug/Release | 18 PASS / 0 FAIL, 112 assertions each |
| Process-only | 23 PASS / 0 FAIL, 597 assertions |
| Frozen before/after numeric pair export | 1,620 pairs byte-identical: status/reasons/witness/parameter/material IDs/tolerance/interior margins/expected contact |
| Independent GEOS | 552 PASS, 346 CLEAR / 201 COLLISION / 5 UNKNOWN; zero false classifications |
| Independent axial volume | 426 PASS, 229 CLEAR / 196 COLLISION / 1 UNKNOWN; zero false classifications |
| Independent native-family volume | 480 PASS, 182 CLEAR / 297 COLLISION / 1 UNKNOWN; zero false classifications |
| Independent motion | 182 PASS, 32 CLEAR / 148 COLLISION / 2 UNKNOWN; 103 collisions missed by three-angle sampling; zero oracle errors |
| Bend Planning | 24 PASS / 0 FAIL, 609 assertions |
| Full native Debug 120/25 + mixed, after flexible ancestry fix | 287 assertions PASS, including source fixture hash/relocation guards |
| Native six-bend Part Debug | 953 assertions PASS, coverage 6/6 |
| Native six-bend Part Release | 953 assertions PASS, coverage 6/6 |
| Final numeric 18x3 / 18x6 / 120x25 | PASS, 12 assertions, complete independent/cold/warm equality |
| Native Assembly Release with capture diagnostics | 287 assertions PASS; baseline Release on same harness also 287 PASS; two earlier intermittent capture failures retained |
| Check Min backend and actual three-Part UI scan | Observed Default quantity 2 = MeetsMinimum, ALT-NG quantity 1 = BelowMinimum, five-bend Part quantity 1 = MeetsMinimum; selected details/markers and alternate-copy cleanup complete |
| Complete legacy UI stress | TIMEOUT / NOT COMPLETED; stopped verified owned harness after more than ten minutes without progress after three-Part UI scan; no full PASS/assertion total claimed |

The new tests invalidate a different plan while the active plan performs a
numeric query; no partial certificate can publish. Parent separation reuse
retains an actual intermediate collision witness in another moving panel.
Observer exceptions preserve geometry results. Existing tests cover workers
1/2/4, mixed occurrence isolation, cache invalidation, cancellation, stale
results, budget exhaustion and exact single-Part equivalence.

The six-bend native runs each independently compare 5,232 STEP/OpenCascade
material points and 12 witnesses, with zero skipped points, false material or
missed material. Original Part input hashes remain unchanged.

## Numeric performance

Final explicit-x64 optimized Release, 32 logical processors. Timings are
observations, not deterministic guarantees; some native regression processes
were active. Counters and semantic comparison are the reproducible evidence.

| Case | Independent ms / queries | Cold ms / queries | Warm ms / queries | Workers |
| --- | ---: | ---: | ---: | ---: |
| 18 tools x 3 bends | 1,247.34 / 4,842 | 3,028.22 / 4,842 | 11.55 / 0 | 1 |
| 18 tools x 6 bends | 5,602.14 / 9,684 | 6,019.25 / 9,684 | 16.68 / 0 | 1 |
| 120 occurrences / 25 geometries | 47,096.66 / 141,840 | 3,162.71 / 29,550 | 28.57 / 0 | 4 |

Baseline six-bend rerun completed in 85,349.21 / 87,062.61 ms independent/cold
with 13,572 queries. Prior Phase 4A.1 runs exceeded 120 seconds; those failures
are retained. Guard-locality alone measured 4,191.58 ms cold with the original
13,572 queries. The final pruning reduces six-bend queries to 9,684 (28.65%).
Three-bend prior baseline: 20,081.25 ms cold / 6,786 queries; current 3,028.22 /
4,842. Prior 120/25: 10,103.82 ms cold / 42,750 queries, warm 18.74 ms; current
3,162.71 / 29,550, warm 28.57 ms. Warm wall time did not improve in this run.
Different timing conditions prevent attributing every wall-time difference
to the patch. No final numeric job exceeds the unchanged 120-second budget.

Final six-bend worker executes 217 full audits / 114.12 ms and 998,569 cheap
shared-guard checks. Query-scoped profiling observes 989,064 active-plan checks
instead of the baseline 123,553,584 checks across all plans. Full boundary
audits are outside those nested per-pair measurements and are counted above.

| Case | Cold geometry/static/process misses | Warm geometry hits/misses | Warm static/process hits | Accounting cache bytes | Sampled managed bytes cold/warm |
| --- | --- | --- | --- | ---: | ---: |
| 18x3 | 1 / 1 / 1 | 1 / 0 | 1 / 1 | 2,370,544 | 10,908,104 / 10,449,984 |
| 18x6 | 1 / 1 / 1 | 1 / 0 | 1 / 1 | 4,724,320 | 16,248,440 / 17,458,448 |
| 120/25 | 25 / 25 / 25 | 1 / 24 | 25 / 25 | 17,812,848 | 75,258,968 / 59,027,912 |

The 120/25 geometry working set exceeds its 16 MiB budget; certificate hits
do not imply geometry hits. Accounting bytes are not a CLR retained-heap size;
sampled GC maxima are not a continuous allocation-profiler peak.

## Phase and independent tool-pair profiling

Instrumented six-bend cold run, 108 evaluations per row. Time is inclusive
engine time, not an additive narrowphase-only CPU measurement.

| Phase | Punch ms / axial queries | Holder ms / axial queries |
| --- | ---: | ---: |
| Approach/load | 195.26 / 324 | 189.79 / 324 |
| Pre-bend | 184.30 / 324 | 187.03 / 324 |
| During bend | 1,773.74 / 1,044 | 1,896.09 / 1,044 |
| Forming end | 186.78 / 324 | 180.34 / 324 |
| Retract/unload | 176.12 / 324 | 176.18 / 324 |

Baseline/final six-bend interval cells and refinements remain 1,512 / 360.
Axial queries fall 6,624 -> 4,680. Instrumentation observes 1,944 stationary
certificate reuses and 1,512 parent-proof reuses (the latter includes checks
avoided by already certified constant panels). The separate multi-moving-panel
test proves parent reuse also works for nonconstant motion. No midpoint is
skipped for an unresolved moving panel.

## Result-read overhead remains material

The benchmark now measures validation separately. For 120/25, full semantic
fingerprinting costs 400 ms for independent rows, 112,176 ms for cold batch
rows and 45,342 ms for warm rows; whole fixture wall time is 234,158 ms.
Held result getters deliberately audit all relevant live batch inputs. Thus
3.16 seconds is solver/coordinator wall time, **not** complete result extraction
or a UI responsiveness promise. Removing these guards without an equivalent
freshness proof would be unsafe; this read-path cost remains outstanding.

## Native STA and stability evidence

Final full Debug: 120 leaf components, 25 source/configurations, 25 cold
captures, zero warm captures / 25 capture hits, four workers, zero worker COM.
Cold wall 81,638.06 ms; capture 72,154.93 ms, inventory/audit 3,259.05 ms;
maximum atomic STA capture 3,947.25 ms. Warm wall 7,658.63 ms, capture .65 ms,
inventory/audit 1,856.16 ms. Selected 10 components contain six sources and
require six captures (18,695.27 ms wall). All 120 outputs match independent
single-Part calls; native company process inputs have zero numeric queries.
These runs prove native integration/gating, not numeric collision speedup.

Two normal siblings are actually captured once; Min grouping keeps quantity
5 / `Đạt`. Reflected, cut and flexible instances retain independent UNKNOWN
reasons, including directly selected flexible descendants. Known bends retain
18 static and 18 process candidates with separate Punch/Holder/phase results.
Source fixture hashes and read-only Part/Assembly state remain unchanged.

Release diagnostics run: cold 60,706.68 ms (capture 51,392.26 ms, max atomic STA
2,443.52 ms), warm 6,642.57 ms (capture .23 ms), capture count 25/0, four workers.
Selected 10 / six sources takes 13,725.38 ms and six captures. Independent
per-instance post-capture gating takes 493.04 ms. Accounting cache 34,777,984
bytes; sampled cold/warm managed heap 91,189,464 / 166,102,352 bytes. The selected
sample is 649,480,880 bytes, an observed GC maximum rather than retained memory.
The same-harness baseline Release run is 73,781.56 / 7,448.39 ms cold/warm,
25/0 captures, max atomic STA 4,724.40 ms. All have zero numeric queries; no
collision-computation speedup or isolated native capture change is claimed.

Fresh fixture generation failed twice before engine execution: native heap
corruption `0xc0000374` / RPC failure at assertions 13 and 15. A cloned verified
fixture, with all references checked inside its new output and source hashes
preserved, enables the successful native Debug run. Two Release attempts
failed the 25/25 capture assertion at 24/25: one Min capture raised
`KeyNotFoundException`. A baseline Release comparison and current Release run
with first-chance logging both complete all 287 assertions and 25 captures;
the exception did not recur in those runs. Its cause is still unconfirmed, not
declared fixed. First-chance/native comparison evidence is retained locally;
the two failed attempts are not relabeled PASS. No Min algorithm is changed to
hide that source error.

Legacy UI stress reached real selected-component details/markers, grouping,
alternate-configuration working-copy cleanup and a complete three-Part form
scan (115 s). It stopped progressing after that form's row/selection checks;
the later many-row layout and Part rebuild auto-update stress steps were not
completed. The verified owned harness was stopped after a sustained no-progress
interval. Cleanup verified the owned SW PID/start but found a document outside
the fixture directory and refused to close that SW process. No arbitrary
process was killed; user sessions were left untouched. This is a remaining
stability issue, not an overall UI regression PASS.

SPLINE uncertainty, missing physical mounts/process plans, native opposite-hand
Mirror Feature provenance and combined flexible/lightweight proof remain
limited by the existing contracts. Actual reflected transforms and standalone
selected lightweight are tested; no fake metadata replaces an unavailable
native state. No manufacturing PASS/NG or winner is emitted.

Bounded incremental face/edge snapshot capture on STA is a proposed future
response improvement, requiring per-batch stamps and complete-publication
guards. It is not implemented here; no COM work moves to a worker.

## Source protection

No Check Min math, WinForms/Designer/event handler, unrelated command or
original CAD/Excel is changed. Private CAD/STEP, source paths/hashes/design
coordinates, review evidence, keys, screenshots and raw logs are excluded
from Git. Only managed code/tests and sanitized documentation are published.
