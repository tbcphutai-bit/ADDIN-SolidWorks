# Unified integration validation — 2026-10-19

Tested implementation: `8cde0d7`, on merge `344d000` of PR #26 and PR #25.
Windows, SOLIDWORKS 2024, .NET Framework 4.8, Debug/Release x64.
Only generic counts and timings are published here. Native CAD, Excel, review
material, source hashes/paths, screenshots and raw logs remain local.

## Build and regression results

| Check | Observed result |
| --- | --- |
| Product Debug x64 | PASS, 0 errors; 16 existing warnings; PE machine `8664` |
| Product Release x64 | PASS, 0 errors; 16 existing warnings; PE machine `8664` |
| Complete managed suite, Debug and Release | PASS, 265 groups / 8,949 assertions each |
| Assembly suite, Debug and Release | PASS, 20 groups / 222 assertions each |
| Process suite, Debug and Release | PASS, 23 groups / 597 assertions each |
| Managed planning | PASS, 24 groups / 609 assertions |
| Form/converter STA suite | PASS, 11 groups; Release 80 assertions, Debug with scaled renders 83 |
| Geometry collision vs GEOS | PASS, 552 cases, 0 false CLEAR/COLLISION |
| Axial envelope independent oracle | PASS, 426 cases, 0 false CLEAR/COLLISION |
| Native-family analytic oracle | PASS, 480 cases, 0 false CLEAR/COLLISION |
| Continuous process-motion oracle | PASS, 182 cases; 0 errors; 103 sparse-three-angle misses retained |
| Frozen process results vs PR #25 | PASS, 1,620 pair records byte-identical: status, reason, witness |
| Native Part, Debug and Release | 953 assertions PASS each, 5,232 independent native B-rep probes; source bytes unchanged; shutdown stability caveat below |
| Controlled numeric UI benchmark | PASS, 6 rendered cold/warm cases; actual numeric worker cancel/close PASS |
| Native Assembly 120/25 Release | PASS, 3,420 assertions; 25 cold captures, 0 warm / 25 hits |
| Native Assembly 120/25 Debug | FAIL, native capture budget exceeded; publication rejected at assertion 58 |
| Native UI copied-seed 0/3/6 lifecycle | FAIL: Release fixture open RPC error; Debug rebuilt-Part Refresh RPC error at check 208 |
| Native UI generated 0/3/6 diagnostic | FAIL: Release expected bend-count check 109; Debug fixture COM/RPC creation at check 11 |
| Close during actual native capture, isolated Release | PASS, 15 checks; scope `ACTUAL_NATIVE_CAPTURE_CLOSE_ONLY`, not a full UI suite PASS |
| Legacy Check Min core | PASS, 130 assertions through grouping/configuration/Min details |
| Legacy UI stress diagnostic rerun | Completed 1,570 checks, report `passed=true`; one retained 1000px layout warning; original timeout NOT RESOLVED |
| Native opposite-hand Mirror Feature | NOT RUN: native creation returned null; reflected transforms tested separately |
| Combined flexible/lightweight fixture | NOT RUN: requested lightweight remained resolved; standalone selected lightweight tested |
| Actual OS 125%/150% DPI | NOT RUN; see scaled-layout evidence below |

The Form/converter suite retains tests for native IDs 7/42/314, zero bends,
missing data, UNKNOWN, COLLISION, phase/Punch/Holder/segment independence,
invalidated sources, Preview/Live separation, Selected scope, cancellation,
refresh, stale results and reentrant close. Its extra checks prove one Form
binding conversion and actual render-stage measurements. Read regressions
compare strict and detached fields cold/warm, reject invalidation, keep other
threads strict and restore the read scope on exceptions.

## Numeric before/after

The before executable was rebuilt from the exact PR #25 commit `c0507bc`.
These controlled fixtures perform actual numeric collision work and carry
simulation provenance. They do not stand in for company CAD or physical setup.

All durations below are milliseconds. Solver means instrumented numeric kernel
duration; it can exceed wall time when workers overlap. Both cold and warm
results match independent per-instance solver status/reason/witness values.

| Fixture | Before cold wall / solver | Unified cold wall / solver | Before / unified warm wall | Cold / warm kernel queries |
| --- | ---: | ---: | ---: | ---: |
| 18 tools × 3 bends | 1,224.52 / 884.15 | 2,717.54 / 1,855.22 | 3.52 / 8.81 | 4,842 / 0 |
| 18 tools × 6 bends | 2,147.97 / 1,611.96 | 4,908.42 / 3,430.55 | 6.03 / 16.78 | 9,684 / 0 |
| 120 components / 25 sources | 1,363.79 / 2,974.27 | 3,189.63 / 6,336.25 | 46.86 / 24.53 | 29,550 / 0 |

The solver is unchanged from PR #25 and the unified cold timings are slower in
this run. Native tests were also running during the unified measurements; the
before/after wall times are not an isolated solver speedup experiment. Query
counts, results and independent witnesses remain equal. No solver speedup is
claimed for the integration.

| Semantic fingerprint read | PR #25 cold / warm | Unified cold / warm |
| --- | ---: | ---: |
| 18 × 3 | 80.01 / 79.05 | 11.46 / 3.32 |
| 18 × 6 | 384.58 / 373.09 | 6.40 / 6.60 |
| 120 / 25 | 56,158.99 / 66,135.19 | 128.34 / 53.59 |

This reproduces the previously reported approximately 112/45-second read-path
problem with a new measured baseline. The bounded read removes repeated
whole-graph audits per field while keeping complete entry/exit validation and
strict retained getters. It does not remove freshness or hash guards.

## Actual controlled numeric Form rendering

These measurements use numeric worker outputs rendered by the real Form, with
zero native COM capture. They are explicitly simulation-only, separate from
the native Live measurements below. Times are milliseconds.

| Fixture | Worker | Solver | Snapshot validation / copy | ViewModel | Grid render | Run → visible |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 18 × 3 cold | 2,649.71 | 1,781.63 | 1.21 / 4.47 | 31.21 | 85.50 | 2,820.62 |
| 18 × 3 warm | 11.96 | 0 | 0.34 / 0.30 | 0.67 | 58.16 | 103.42 |
| 18 × 6 cold | 5,423.34 | 3,742.46 | 1.33 / 1.28 | 4.28 | 98.95 | 5,555.34 |
| 18 × 6 warm | 23.30 | 0 | 1.27 / 1.40 | 2.56 | 90.89 | 167.28 |
| 120 / 25 cold | 3,343.14 | 6,383.08 | 8.67 / 7.91 | 14.16 | 78.81 | 3,476.17 |
| 120 / 25 warm | 59.25 | 0 | 9.96 / 11.60 | 10.38 | 75.43 | 207.81 |

## Actual SOLIDWORKS Live results

The native fixture has 120 leaf components under two subassemblies and 25
source/configuration identities. No reviewed company process/setup is invented;
native numeric solver queries are **zero**, and all 18 candidates per known
bend retain UNKNOWN geometry/process where evidence is absent.

Release native backend cold/warm wall: 97,475.36 / 13,253.60 ms. Cold capture:
83,167.42 ms; inventory: 6,008.13 ms; maximum atomic STA capture: 4,454.60 ms.
Selected ten components contain four sources and require four captures,
12,030.76 ms wall. Warm performs zero captures / 25 hits. Read-only source
hashes/state and zero worker COM accesses are asserted. Statuses and exact
reasons match independent per-instance results and cold/warm output.

The real workflow opens Check Chấn, presses Assembly Batch, runs Entire and
clicks each Part and each Bend to inspect all 18 candidates. The native 120/25
Form cold/warm measurements are:

| Stage | Cold ms | Warm ms |
| --- | ---: | ---: |
| Native capture | 38,796.43 | 0.25 |
| Managed worker | 282.96 | 121.30 |
| Numeric collision solver | 0 | 0 |
| Snapshot validation | 0.12 | 0.05 |
| Snapshot result copy | 107.33 | 113.50 |
| ViewModel conversion | 146.88 | 131.65 |
| Grid render/paint | 45.02 | 38.25 |
| Run → visible | **89,286.23** | **7,944.54** |

The native Form run has a fresh session, so its cold capture cost is distinct
from the preceding native backend benchmark. Run-to-visible also includes
native inventory/freshness, local file/catalog validation and scheduling.
The remaining multi-second warm path is not ViewModel conversion. No guard is
removed to hide it, and no sub-50 ms atomic capture promise is made.

## Failed attempts and unresolved work

**KeyNotFoundException at 24/25: NOT RESOLVED.** The successful Release 25/25
run with first-chance STA dictionary-exception logging did not reproduce it.
No matching exception stack was produced. Existing edge identity dictionaries
were inspected, but a suspected lookup is not evidence of the original cause;
no speculative Check Min change is included. Further reproducible native
capture evidence is required before calling this fixed.

The first native Assembly Release attempt lost its owned SOLIDWORKS RPC server
before numeric execution and is recorded FAIL. A small native UI rerun lost
the RPC server during fixture `OpenDoc6` at assertion 11 and is also FAIL.
These failures are retained independently of later successful runs.

Native Assembly Debug also failed with
`ASSEMBLY_NATIVE_TIME_BUDGET_EXCEEDED` / `STALE_ASSEMBLY_OR_SCOPE`; the original
300-second native budget was not increased to turn that run into PASS. No stale
result was published. Additional private per-capture before/after logs now
record progress, capture errors and maximum atomic STA duration for further
diagnosis without changing the capture or solver algorithm.

Windows Application Error events for owned failed/shutdown test instances
recorded `0xc0000374` in `ntdll.dll`, including the UI fixture-open failure and
the Debug budget run. An owned library-test shutdown also has such an event
after its 953 completed assertions. Those assertion results do not establish
clean native shutdown. The initiating native fault and the earlier dictionary
exception are still unconfirmed; neither is declared fixed here.

The initial small UI run completed the launcher, real 0/3/6 entire/warm and
Selected, cancel/restart and stale Part withdrawal, then failed Refresh after a
child rebuild. Its original failure message did not identify the cause. The
extended regression rebuilds the private owning Assembly and includes the
backend diagnostic. In the serial Debug rerun it again reached check 208 and
failed that Refresh because the SOLIDWORKS RPC server was unavailable. Thus an
Assembly rebuild alone is not a demonstrated fix. Serial Release again failed
at copied-seed `OpenDoc6` before the backend ran.

The original copied-seed regression remains the default. An additional
`ADDIN_NATIVE_UI_GENERATED_SIX=1` mode requests six native profile corners,
alongside the existing zero/three-bend fixtures, to isolate the Live lifecycle
from imported-seed behavior. Its Release run failed the expected 0/3/6 captured
count assertion at check 109; its Debug run lost RPC during fixture creation at
check 11. It is an experimental diagnostic, not a validated substitute for the
copied seed. The actual count discrepancy remains unconfirmed; no counter or
FeatureId was changed to force a pass. New private inventory/capture diagnostics
record per-Part reason, native row count and placement error for reproduction.

The isolated native capture-close regression uses a fresh, real three-bend
Part and a selected-component Live backend. A managed-only observer posts
`Form.Close` after native capture has begun, while the item is not yet Done.
The Form defers closing until cancellation is acknowledged and safely disposes.
The Release run passed all 15 checks, including original seed hash protection.
It runs with `ADDIN_NATIVE_UI_CLOSE_ONLY=1`, excludes unrelated fixture creation,
and explicitly reports `ACTUAL_NATIVE_CAPTURE_CLOSE_ONLY`. It does not relabel
any full small-suite failure as PASS. The separate controlled numeric case also
passed actual worker cancel/close.

Legacy UI stress first reached 130 passing core assertions, then failed its
45-second scan deadline. The diagnostic rerun uses the existing bounded native
test message pump and a 180-second scan deadline, without changing product
source or removing any stress assertion. Its three-Part scan took 95.2 seconds;
the complete 1,570-check run reached row selection, many-row layout/search,
automatic Part rebuild, selected-bend preservation, NG numbering/3D markers
and source-hash protection. The report is `passed=true`, with one baseline
layout warning retained separately. This resolves the too-short scan allowance
in the diagnostic harness, but does not establish the cause of the previously
reported sustained legacy UI hang: **original timeout NOT RESOLVED**, not
reproduced with the bounded pump. A production stability fix is not claimed.

## Antigravity layout handoff and acceptance

Actual native 96-DPI screenshots are retained locally. At the default size,
Selected Components and Preview labels are clipped, the current Bend selector
can overlap its FeatureId heading, and tool reason/manufacturing text is
truncated. Antigravity should adjust those regions and verify diagnostics and
18-tool navigation at supported window sizes. No Designer/style rewrite is
included here.

The legacy stress also reports one existing 1000px warning: a wrapped material
cell needs 57px while its row is 53px. Other row selection, search, rebuild and
marker checks completed. That layout warning is not silently treated as PASS.

Form scaling at factors 1.0, 1.25 and 1.5 was rendered and usable-control checks
passed; the test process still reported DeviceDpi=96. This is scaled-layout
evidence, **not** actual Windows 125%/150% per-monitor DPI certification.

Before acceptance: resolve/reproduce the native dictionary/RPC failures and
Debug 120/25 native budget overrun, complete the small native rebuild/refresh
suite, diagnose the experimental fixture's count discrepancy, and confirm the original legacy
stress timeout under the production host message loop, validate
actual 125%/150% DPI and Antigravity layout fixes, and obtain reviewed physical
mount/process evidence for production collision use. Native reflected/flexible/
cut states remain independently UNKNOWN under their existing contracts; native
Mirror Feature provenance and combined flexible/lightweight remain unverified.
No manufacturing PASS, winner tool, remote merge, or original CAD/Excel change
is part of this handoff.
