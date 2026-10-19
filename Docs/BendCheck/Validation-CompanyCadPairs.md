# Company Punch–Holder pairs validation

Date: 2026-10-19. Baseline: PR #32,
`12189427d5069813d566d96f877df5685cb854bc`.
Branch: `feature/check-chan-company-punch-holder-pairs`.
Draft target: `feature/check-chan-live-provider-native-stability`. No merges.

## Delivered behavior

Check Chấn can now load both roles directly from one company Master DXF, with
exactly 18 fixed, source-pinned Punch–Holder associations. Every source INSERT
is selected by authored handle/block/layer identities and corroborated against
its explicitly drawn presentation cell. The Punch ID must also match its
company TOOL_* block name. There is no nearest-holder runtime matching.

The guarded Part/Assembly UI read snapshot carries the same pair identities to
the existing 18 candidate rows and evidence area. Phase 4B can consume an
explicitly reviewed simulation pose of those exact source selections. It cannot
silently replace them with arbitrary fixture curves, even when the fixed
catalog is missing or invalid. Equivalent hardware identity remains usable
across independent native bend bindings for a common-tool route.

Source entities/transforms remain unchanged. Raw multi-outline/SPLINE Holder
geometry remains available for inspection; collision material requires its own
reviewed solid/hole/reference classification and uncertainty bands. Source CAD
association does not approve tip, seating, clamp installation, physical length,
process motion or manufacturing capability. Holder material revocation also
withdraws any held CAD-reference simulation occupancy immediately.

The existing preference remains a proven complete common-tool sequence first,
then minimum changes. This work does not add multi-tool changeover planning.

## Company source

The folder supplied by the user contains `KIKUKAWA_18_DAO_CLAMP.dxf` (997,448
bytes). This is the actual file read; a separate `(1)` file was not present.
Its SHA256 and the exact authored mapping SHA256 are retained in the private
catalog/reports and exposed by the existing local UI evidence. No claim is made
that a nonexistent filename is a separately verified revision.

The default local source settings now point to
`%LOCALAPPDATA%\ADDIN\BendTools\CompanyPairs\placement.json`; the private fixed
manifest is `pairs.json` in that directory. Previous settings are backed up if
present. The linked DXF and Excel are read-only and unchanged. No installed DLL,
COM registration, startup preference or CAD/Excel file is overwritten.

The actual-source regression verifies every selected entity, analytic curve
inventory, transform and source SPLINE data. It deliberately swaps two real
same-block Holder INSERTs in a separate private mapping, verifies both fail
their source-cell check, and verifies the other 16 remain identified. Both
actual Punch/Holder root sets contain 18 unique INSERTs.

| Tool | Fixed CAD association | Punch material region | Holder source retained | Physical/process result |
| --- | --- | --- | --- | --- |
| AMD-STD-003 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-004 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-0048 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-0049 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-008 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-045 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-047 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-103 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-109 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-117 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-200 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-202 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-210 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-452 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-STD-453 | MATCHED | Valid, closed | Complete, includes SPLINE | UNKNOWN |
| AMD-202662 | MATCHED | Valid, closed | Complete | UNKNOWN |
| AMD-204662 | MATCHED | Valid, closed | Complete | UNKNOWN |
| KIK-7181328001 | MATCHED | OPEN, not repaired, invalid material | Complete, includes SPLINE | UNKNOWN |

All 18 Holders retain CAD-reference semantics. None obtains a manufactured
physical mounting certificate. The original open KIK polyline is unchanged.
Actual CAD selections/mapping and coordinate-bearing reports remain local;
only generic code, synthetic tests and this sanitized summary are committed.

## Test matrix

Subsets are included in full suites; they must not be added as independent totals.

| Test | Result | Actual evidence |
| --- | --- | --- |
| Product Debug/x64 and Release/x64 | PASS | Zero build errors; isolated outputs; build hooks/COM registration disabled |
| Full managed Debug/Release | PASS | 302 tests / 9,112 assertions each; zero failures |
| New CAD pair regressions | PASS | 15 tests / 64 assertions each; exact selection, swaps, all geometry, stale/revocation, source-bound numeric simulation, missing/unreadable catalog |
| Company CAD catalog Debug/Release | PASS | 16,977 assertions each; 18/18 fixed associations; 17 valid Punch regions; KIK open |
| Assembly-only/mixed managed | PASS | 20 tests / 222 assertions in each configuration |
| Bend Planning | PASS | 24 tests / 609 assertions |
| Existing/new WinForms UI | PASS | 16 groups / 148 assertions each; source identities, no promotion; existing layout retained |
| Independent GEOS 2D | PASS | 552 cases, false CLEAR=0, false COLLISION=0 |
| Independent axial oracle | PASS | 426 cases, false CLEAR=0, false COLLISION=0 |
| Independent native-family oracle | PASS | 480 cases, false CLEAR=0, false COLLISION=0 |
| Independent motion oracle | PASS | 182 cases, 103 sparse-angle misses demonstrated, zero discrepancies |
| Frozen PR #32 numeric reference | PASS | 1,620 byte-identical pair records; 13,932 queries, 2,268 intervals, 540 refinements; exact status/reason/witness/tolerance equality |
| Numeric 18x3, 18x6, 120/25 benchmark | PASS | 12 assertions; cold real numeric work and warm exact proof reuse agree with independent per-instance checks |
| Native Part 0/3/6, Debug/Release | Functional PASS | 219 assertions each; actual CAD pair metadata, Min equivalence, cold/warm, rebuild, source unchanged |
| Native Assembly 120/25, Debug/Release | Functional PASS | 291 assertions each; all/selected, cache, mixed occurrence isolation, Min grouping; Debug also reads default catalog |
| Native existing UI/Part/Assembly, Debug | Functional PASS | 680 assertions; launcher, 0/3/6, mixed, selected/all, Cancel, Refresh, stale, close during capture |
| Legacy source mode/native six-bend geometry | Functional PASS | 953 assertions; continuous coverage 6/6; 5,232 independent STEP reference points, 12 witnesses |
| Native clean server shutdown | NOT_CONFIRMED / acceptance FAIL | Owned processes did not confirm zero exit within the existing audit; harnesses return nonzero |
| Physical production acceptance | UNKNOWN | No reviewed company process/setup/material-role package available; no manufacturing PASS or selected production tool |

## Native behavior and timing

The Debug Part run also reads the installed default local catalog configuration,
without a placement-path override from the harness. All 18 pair bindings reach
the actual UI and backend. Original Part/Assembly/DXF/Excel sources are not saved.

Observed Part run-to-visible times (seconds):

| Part | Debug cold | Debug warm | Release cold | Release warm |
| --- | ---: | ---: | ---: | ---: |
| 0 bends | 1.432 | 0.620 | 1.298 | 0.515 |
| 3 bends | 2.839 | 0.620 | 3.009 | 0.616 |
| 6 bends | 4.924 | 0.654 | 4.572 | 0.660 |

Assembly Release: cold 48.230 s, warm 6.987 s, selected-10 15.426 s.
Cold captures 25/25 source groups; warm captures 0 and hits 25. Peak workers=4;
cold geometry hits/misses=95/25, warm=120/0. Independent per-instance worker
baseline=0.985 s. Native capture dominates cold time (41.099 s).
Assembly Debug: cold 50.175 s, warm 10.503 s, selected-10 9.263 s; cold
captures=25, warm capture hits=25, zero numeric queries in both configurations.

## Numeric benchmark

This is the inherited controlled geometric/process fixture, not a claim of
physical company setup verification. Every cold row below completes numeric
work and agrees with the independent per-instance solver, including witnesses
and reasons; warm rows reuse those exact certificates. The old PR #32 timing
is included for traceability. Different machine workload affects these short
runs; this PR does not claim a new collision-algorithm speedup.

| Case | PR #32 cold / warm (s) | Current independent (s) | Current cold / warm (s) | Independent / cold / warm queries |
| --- | ---: | ---: | ---: | ---: |
| 18 tools x 3 bends | 3.070 / 0.0119 | 1.154 | 1.199 / 0.0060 | 4,842 / 4,842 / 0 |
| 18 tools x 6 bends | 6.806 / 0.0194 | 2.283 | 2.430 / 0.0085 | 9,684 / 9,684 / 0 |
| 120 instances / 25 sources | 3.744 / 0.2004 | 17.930 | 1.585 / 0.1417 | 141,840 / 29,550 / 0 |

Peak workers=1 for the single-Part 18x3/18x6 cases, 4 for 120/25. Cold peak
managed memory=12.25 / 17.77 / 77.44 MiB respectively. Cold geometry/static/
process misses=1/1/1, 1/1/1, 25/25/25. Warm static/process hits=1/1, 1/1,
25/25 respectively. The warm 120/25 geometry cache has 1 hit and 24 misses
(bounded geometry residency), while all 25 static/process proofs are reused.
No current numeric case exceeds the inherited 120-second concern; no timeout,
tolerance, interval count or intermediate-state requirement was relaxed.

## Native limits

These native runs have **zero numeric collision queries** because the real
process/setup evidence is missing. They prove inventory, capture, source/UI
binding and conservative UNKNOWN behavior, not collision-solving acceleration
or manufacturing capability. Native UI timings can include other concurrent
validation workloads and are observations, not a performance optimization claim.

The existing opposite-hand mirror feature API returned null; that exact feature
case remains NOT RUN. Actual reflected transforms are tested. A request for
lightweight state inside the flexible fixture returned native state 2; that
specific combination remains NOT RUN. The separate actual lightweight test,
suppressed/flexible/cut/subassembly/configuration cases are exercised. Valid
normal instances still execute native capture independently of blocked siblings.

The PR #32 native shutdown limitation remains unresolved. No heap-corruption,
OpenDoc6/RPC or in-process installed-add-in stability fix is claimed here. Native
functional PASS rows must not be read as a clean-lifecycle PASS. No timeout was
raised, no test was skipped to convert a failure into a PASS, and no user
SOLIDWORKS process/installed DLL was terminated/replaced by the build.

## Remaining evidence

- Holder solid/hole/reference material roles where multiple outlines exist.
- Independent physical Punch/Holder mounting datums, seating and real axial span.
- Registered whole-sheet before/after process states, motions, constraints and
  forming-contact evidence for an actual Part.
- A valid closed material source for KIK, supplied by the owner if required;
  this implementation will not close the existing source for them.
- Resolution/verification of the inherited native clean-shutdown issue.

Until these are supplied, real-tool process/collision rows and production tool
selection stay UNKNOWN. `WinnerToolId` remains null. See [the input contract](CompanyCadPairs.md)
for fixed catalog and explicitly reviewed CAD-reference simulation inputs.
