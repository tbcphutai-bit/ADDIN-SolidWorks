# Live process provider and native stability validation

Date: 2026-10-19 (Asia/Bangkok). Windows x64, SOLIDWORKS 2024 SP5,
.NET Framework 4.8, C# 7.3, Visual Studio 18 MSBuild.
Baseline: PR #30 at `07d29bd3af2dc17787fd276eb59beee97645a518`.
Branch: `feature/check-chan-live-provider-native-stability`.
Draft base: `feature/check-chan-phase4b-unified`. No merges.

## Acceptance

The provider/importer now reads independently reviewed process packages and
executes the existing numeric planner through the existing Part/Assembly
backend. Source/configuration/revision/registration/setup gates remain strict.
The default preference is a proven complete common-tool route: one tool type
first, then minimum changes. Current search supports one unchanged tool/setup;
it does not invent a multi-tool changeover process.

**Production acceptance is BLOCKED**: no physically reviewed company process,
datum/seating/extents and registered before/after state package was available.
The importer is exercised with controlled signed simulations; these are
explicitly SIMULATION. `WinnerToolId` remains null; manufacturing status stays
`CHUA_XAC_MINH` for all real tools.

**Overall native stability acceptance is FAIL**. Functional Part and Assembly
checks execute, but clean server shutdown is not confirmed and baseline native
dumps contain heap corruption. Source fixes remove proven timer/static roots
and borrowed-RCW release defects; they are not claimed to eliminate the entire
native failure until the new in-process build can be isolated and verified.

## Actual tests

Subsets are included in full suites and are not additional independent totals.

| Test | Result | Actual evidence |
| --- | --- | --- |
| Product Debug/x64 and Release/x64 rebuild | PASS | Zero errors; existing product warnings retained |
| Managed, UI and native test targets, Debug/Release | PASS | Isolated outputs; no registration/build events or user-session termination |
| Full managed Debug | PASS | 287 tests / 9,048 assertions, zero failures |
| Full managed Release | PASS | 287 tests / 9,048 assertions, zero failures |
| Importer/provider cases in the full suite | PASS | 11 tests / 54 assertions; actual file import and real numeric queries |
| Explicit Assembly subset, Debug and Release | PASS | 20 tests / 222 assertions each, including mixed occurrence isolation |
| Bend Planning regression | PASS | 24 tests / 609 assertions |
| Existing/new WinForms navigation and scaled renders, Debug/Release | PASS | 15 groups / 142 assertions each; native IDs, imported simulation label, proven default selection, stale withdrawal |
| Independent GEOS 2D / axial / native-family / motion | PASS | 552 / 426 / 480 / 182 cases, zero false results |
| Frozen PR30 numeric reference | PASS | 1,620 pair records byte-identical; 13,932 queries, 2,268 intervals, 540 refinements |
| Numeric Assembly benchmarks | PASS | 18x3, 18x6 and 120/25 compare against independent per-instance execution |
| New supplied clamp Master, explicit Punch-only audit | PASS | 9 assertions; 18 identified/read, 17 closed valid regions; no inferred holder pairing |
| Native Part 0/3/6 Release | Functional PASS / lifecycle FAIL | 195 assertions; exact Min, native IDs/highlight, cold/warm, rebuild/Refresh, unchanged source |
| Native Part 0/3/6 Debug | Functional PASS / lifecycle FAIL | 195 assertions, same tested workflow |
| Native Part 0/3/6 with the newly supplied clamp Master | Functional PASS / lifecycle FAIL | Additional 195 assertions; exact new-source Punch selections reach all 18 native tool slots; missing Holder evidence remains UNKNOWN |
| Native 120 components / 25 source/configurations Release | Functional PASS / lifecycle not accepted | 288 assertions, including 170 core; run before final diagnostic-only capture flag |
| Native 120/25 Debug | Functional PASS / lifecycle FAIL | 288 assertions; final audit FunctionalExitCode=0, shutdown NOT_CONFIRMED |
| Baseline timer/lifetime A/B | Expected FAIL | 2 assertions reached; actual live pane timer remains after shutdown |
| Current timer/lifetime A/B | Functional PASS / lifecycle FAIL | 8 assertions: disposed timers/command roots, cleared static app, borrowed RCW still usable, repeated disconnect |
| Native UI final run, Release | Functional PASS / lifecycle FAIL | 581 assertions, existing launcher and 0/3/6 + mixed Assembly workflows; atomic capture close actually observed |
| Native six-bend library/geometry, Debug product | Functional PASS / lifecycle FAIL | 953 assertions; all six continuously covered, one native body read, zero worker COM queries |
| Independent actual native STEP / OpenCascade | PASS | 5,232 points, none skipped; 12 interior witnesses, six finite clear probes; no false/missed material |
| Legacy native Check Min/Assembly/UI stress | Functional PASS, inherited display warning | 1,570 assertions, including 130 core; no original source change; one pre-existing wrapped material row warning |
| Actual Windows 125%/150% DPI | NOT RUN | DeviceDpi=96; scaled-control renders are not OS DPI tests |
| Reviewed company process on native Part | BLOCKED | Required physical/process evidence absent; not substituted with simulation |

The import tests cover pinned revisions/package/authority, duplicate source
mapping, stale native geometry/stamp/PersistentId/frame, missing registration,
missing motion errors, open material, unsupported SPLINE process material,
path traversal, one invalid tool isolated from valid siblings, cancellation,
cold/warm cache reuse, changed source withdrawal, provider disposal, recovery
after a missing artifact returns, and explicit forming-contact bindings.
Malformed provider paths withdraw held evidence rather than merely faulting
the background audit and leaving a previous proof current.
All 18 candidates survive missing data. The numeric fixture proves different
orders can produce a real interior collision or a complete CLEAR path. Such a
path is preferred only while current and only in its checked scope.

## Native failure investigation and A/B

Microsoft ProcDump captured private full dumps of the exact owned baseline
SOLIDWORKS PID. Microsoft WinDbg/CDB and SOS inspected the native and managed
stacks/roots. Tools were downloaded from official Microsoft sources; dumps,
raw stacks containing local paths, source snapshots and symbols remain local.

The baseline dump reports:

`HEAP_CORRUPTION_ACTIONABLE_BlockNotBusy_DOUBLE_FREE_c0000374_ucrtbase.dll!free_base`

The stack reaches `RtlFreeHeap`, `ucrtbase!free_base`, native SOLIDWORKS taskpane/
dispatch release frames, `clr!SafeReleasePreemp`, `RCW::ReleaseAllInterfaces`,
`RCWCleanupList`, `CoEEShutDownCOM` and application shutdown. Public symbol
names do not prove the exact private native destructor that corrupted memory.

SOS verifies two actionable source defects:

- `SwAddin.InstanceSwApp` remains a strong application RCW root after the
  instance fields have been cleared. Disconnect now clears this static root,
  retires fields before reentrant native teardown and does not manually release
  the host's borrowed/shared application RCW.
- An active `makeHoleUpdateTimer` roots a disposed `BomTaskPaneControl`, its
  `CheckBalloon` command and the application RCW. Shutdown now first stops,
  unsubscribes and disposes the monitor and other timers/native subscriptions,
  clears command roots and is idempotent. The baseline/current native lifetime
  A/B actually fails/passes the corresponding assertions.

The installed in-process startup add-in in the dumps is **a different DLL path
from the isolated product build under test**. Other add-ins also retain COM
roots. No installed DLL, COM registration, startup preference or third-party
add-in was overwritten to make the test pass. Unloading the installed add-in
after startup in an owned test process did not eliminate shutdown corruption.
The exact initiating native double-free is therefore still unresolved; clean
in-process A/B with the new build remains required.

For Name2/OpenDoc6 RPC, the out-of-process harness previously treated ROT
registration as startup completion. It now waits for the documented
`StartupProcessCompleted` within the unchanged 60-second startup deadline.
The owned-session optional `CommandInProgress` scope is saved/restored. It
reduces native UI/update overhead for out-of-process API sequences; it is not
a product event-handler change or evidence that every RPC cause is fixed.

| Controlled native condition | Observed result |
| --- | --- |
| Baseline startup behavior / 120 fixture | RPC at Name2, 56 assertions reached, no planner proof |
| Baseline DLL with readiness only | Inventory reaches all 120; inherited 300-second native job budget expires, 58 assertions reached |
| Current DLL, readiness and out-of-process command scope | Full 120/25 functional suite executes: 288 assertions in Release and Debug |
| Baseline Part, including owned-session unload experiment | 195 functional assertions, shutdown NOT_CONFIRMED; full dumps show double-free |
| Current Part and lifetime fixes | 195 functional / 8 lifetime assertions; shutdown still NOT_CONFIRMED |

No timeout or collision tolerance was increased. RPC is a symptom boundary;
these observations do not establish a unique initiating native fault.
`KeyNotFoundException` did not reproduce through the full native 120/25 path.
Optional topology/placement capture now retains full exception phase/stack
instead of only the exception type. Its original root cause is **unproven**,
not marked fixed and not hidden by fabricating geometry.

The first current native UI run completed launcher, 0/3/6 inventory,
Selected/Entire, cold/warm, Cancel, Refresh, stale Part/pose and ordinary close,
then failed the atomic-capture-close test. Investigation found its observer
assumed the old pre-capture index increment. Async provider reads now advance
the index afterward, so that observer could never post Close. The harness
uses an explicit volatile capture flag and no longer inspects owner/items on
its observer thread. The assertion is retained, not skipped or relaxed.

The next native UI run passed that capture-close assertion, then correctly
rejected the mixed-edge fixture's supposedly ordinary sources: the caller's
seed had three out-of-context external sketch references (status 4 on each).
Unlike the existing 120/25 fixture, that UI fixture had not prepared independent
source definitions. Fixture preparation now breaks external references only on
the two explicitly bounded disposable copies, rebuilds/saves those copies and
checks original source hashes. It does not weaken inventory eligibility or
change company CAD. The complete rerun passes 581 functional assertions,
including actual capture and 18-tool rows for normal siblings alongside the
individually isolated reflected/cut/flexible instances.

## Performance

Single-workstation measurements, some overlapping diagnostic/test workloads;
not statistically isolated algorithm speedup claims. The API command scope
is an out-of-process harness setting, not a promise of identical production
taskpane latency. Missing company setup/process data makes native numeric
queries zero; those runs do not measure faster fresh collision calculation.

| Real Part | Cold visible ms | Native capture ms | Warm visible ms |
| --- | ---: | ---: | ---: |
| 0 bends, current Release | 2,450 | 537 | 748 |
| 3 bends, current Release | 5,214 | 3,316 | 823 |
| 6 bends, current Release | 5,061 | 3,850 | 611 |
| 6 bends, PR30 audited Release | 32,314 | 27,633 | 863 |

The additional native run using the newly supplied clamp Master snapshot
passes 195 functional assertions. Its six-bend Part cold/warm visible time is
**6,207 / 860 ms**. Its clean shutdown remains NOT_CONFIRMED. The new drawing
does not grant missing Holder mapping or process evidence simply by loading.

Native Release 120/25: cold **73,726 ms**, capture **64,912 ms**, inventory
**2,517 ms**; 25 captures, geometry 95 hits/25 misses, four peak workers.
Warm **8,159 ms**, zero captures/25 capture hits, geometry 120 hits/0 misses.
Selected 10 instances/6 sources: **16,727 ms**, 6 captures. Debug cold/warm:
**82,090 / 8,905 ms**, 25/0 captures; selected **19,400 ms**, 6 captures.
Maximum atomic capture: 3,981 ms Release, 3,588 ms Debug. Cancellation remains
nonpreemptive inside one native call. COM is never moved to workers.

Release native cache accounting: 34.75 MB cold, 40.93 MB warm; sampled managed
heap 52.76 / 102.00 MB. Debug sampled managed heap 92.68 / 245.03 MB.
These are samples and weighted cache sizes, not exact process peak memory.
Unreviewed occurrence programs are deliberately not shared as collision proofs:
native static/process misses remain 120, and numeric queries remain zero.
The independent native per-instance worker reference is 1,406 ms Release /
1,784 ms Debug with zero queries, not a numeric acceleration comparison.

| Controlled numeric fixture | Independent ms / queries | Cold ms / queries | Warm ms / queries |
| --- | --- | --- | --- |
| 18x3 | 1,114 / 4,842 | 3,070 / 4,842 | 12 / 0 |
| 18x6 | 5,879 / 9,684 | 6,806 / 9,684 | 19 / 0 |
| 120/25 | 48,267 / 141,840 | 3,744 / 29,550 | 200 / 0 |

No measured numeric fixture exceeds the inherited 120-second process budget.
Cold 18x3 and 18x6 are slower than their sequential references; they are not
called numeric speedups. Warm zero-query results measure certified reuse.
120/25 uses four workers and 17.81 MB weighted cache; cold/warm sampled managed
heap 82.93 / 60.24 MB. Warm static/process cache hits are 25 each; its bounded
geometry working set still has 25 misses. Pair status/reason/witness/error
semantics remain identical to the frozen pre-change reference.

## Newly supplied clamp library

The user-supplied `KIKUKAWA_18_DAO_CLAMP.dxf` was read as a stable private
snapshot without changing the locked original. Exact unique ToolId TEXT and
`TOOL_<ToolId>` block INSERT identities select its 18 Punches. No coordinate,
INSERT order or nearest-holder guess is used. The previous Master manifest
fails on this source and is not silently reused.

There are 18 other root reference INSERTs. The drawing explicitly describes
added clamps as CAD references. No reviewed source-bound Punch/Holder mapping,
physical seating or datums accompanies this revision. All 18 holder mappings
and setups therefore remain unverified. The private audit leaves them
unassigned, rather than reusing the old source's 14 reference associations.

| ToolId | Punch read | Closed valid region | Holder/setup physical evidence |
| --- | --- | --- | --- |
| AMD-STD-003 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-004 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-0048 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-0049 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-008 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-045 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-047 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-103 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-109 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-117 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-200 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-202 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-210 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-452 | Yes | Yes | Missing / UNKNOWN |
| AMD-STD-453 | Yes | Yes | Missing / UNKNOWN |
| AMD-202662 | Yes | Yes | Missing / UNKNOWN |
| AMD-204662 | Yes | Yes | Missing / UNKNOWN |
| KIK-7181328001 | Yes | **No: open original** | Missing / UNKNOWN |

No company CAD/Excel, coordinates, private mapping manifests, signing keys,
raw dumps, native screenshots or source snapshots are committed. Original
seed/fixture hashes are checked by the native tests. Check Min math and
WinForms Designer are unchanged.

## Remaining evidence and user verification

1. A clean isolated **in-process new-build** lifecycle run is still required.
   Do not treat passing external-client cleanup assertions as a resolved
   SOLIDWORKS heap-corruption defect.
2. Supply independently checked datums, Punch/Holder correspondence, seating
   inspection and finite axial extents for this new library revision.
3. Supply one actual native 2–3-bend process/setup package: registered complete
   sheet states, process motions, contact zones, constraints and approvals.
   The importer/schema is implemented, but a production process is not inferred.
4. General nonparallel process registrations, arbitrary process B-reps/SPLINE
   material and multi-tool changeover sequences remain unsupported/UNKNOWN.
5. Native opposite-hand mirror feature creation returned null (NOT RUN);
   actual reflected occurrences are tested. Requested lightweight inside a
   flexible subassembly resolved to native state 2 (NOT RUN for that specific
   combination); standalone native lightweight behavior is tested. Native
   full 120/25 checks include normal/reflected, Assembly Cut, flexible ancestry,
   suppressed, virtual, multibody, alternate configurations and repeated names.

See [provider contract and authoring workflow](ReviewedProcessProvider.md).

## Completion addendum

The final native UI route runs the existing launcher, real 0/3/6 inventories,
Entire and Selected scope, preview separation, native FeatureId binding and
all 18 tool slots per bend. It exercises cold/warm cache, cancel/restart,
saved selection/Refresh, stale child rebuild, stale instance pose, ordinary
close and reentrant close posted during real atomic native capture. The same
workflow runs on the native mixed-edge Assembly. All **581 functional
assertions pass**. The exit audit still records FunctionalExitCode=0 and
ShutdownResult=NOT_CONFIRMED, so the complete native lifecycle run exits FAIL.
Real 0/3/6 Assembly cold/warm Run-to-visible: **10,176 / 1,555 ms**; mixed
Assembly: **9,974 / 2,426 ms**. Initial launcher Run-to-visible is **21,675 ms**,
including first-run source/UI initialization. These are zero-query real-data
gating runs, not company process simulation timings.

The six-bend Debug product/library route passes **953 assertions** and its
independent STEP/OpenCascade reference checks **5,232 points**, zero skipped,
12 collision witnesses and six independently separated finite probes. Native
exact extrema and independent STEP bounds differ by at most approximately
`1.8e-15 mm`. All six envelope coverage certificates remain continuous; no
sampled-slice certificate replaces native material. Clean server shutdown is
again NOT_CONFIRMED and the harness records overall failure. An earlier attempt
failed at 212 assertions because the explicit Python/script oracle variables
were omitted; the corrected full run supplies the existing independent oracle
and retains every check.

Legacy Check Min/Assembly/UI stress passes **1,570 functional assertions**.
One inherited legacy-window display warning remains at 1000 px: a wrapped
material value requires 57 px while the existing row is 53 px. This is reported
and not counted as a new Phase 4B layout fix. Phase 4B scaled-control renders
pass in both Debug and Release at DeviceDpi=96; actual OS high-DPI remains
NOT RUN. The old native/source CAD and Excel are not overwritten.

One initial sandboxed native launch failed before any assertion with a Windows
process-creation error. Authorized native runs outside the app-container
sandbox are the actual Part/Assembly evidence above. No process-creation
failure, missing oracle, failed fixture assertion or native shutdown failure
is silently counted as PASS.
