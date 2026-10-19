# Phase 4A.1 validation — 2026-10-18/19

This file preserves the original `73a4566` validation history. The Windows
follow-up to ChatGPT's `5f1e468` mixed-occurrence fix, additional defects and
current observed results are in [Mixed Isolation validation](Validation-Phase4A1-MixedIsolation.md).

Issue [#23](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/23).
Baseline PR #22, `6717848b734a032b6317155556ab6183c8d54042`.
Branch `feature/check-chan-phase4a1-assembly-batch`; Draft target
`feature/check-chan-phase4a-motion-states`. No previous PR/main is merged or
edited. Results below are observed local Windows runs, not CI projections.

The implementation is an executable backend, native-tested through
`BendCheckSession.BeginAssemblyBatch / Pump / Completion`, with reusable Min
capture, managed single-flight caches, global worker budgeting and complete
18-tool static/process results. See the [API/cache/freshness contract](Phase4A1-AssemblyBatchContract.md).
The existing button is **not wired** to start it; no UI handler/Designer is
edited. Antigravity can consume this API from the current command.

## Builds and regression

| Check | Observed result |
| --- | --- |
| Product Debug/x64 and Release/x64 | PASS, 0 errors; 16 inherited warnings each |
| Full managed DEBUG | **258 PASS / 0 FAIL, 8,799 assertions** |
| Full optimized TRACE-only Release | **258 PASS / 0 FAIL, 8,799 assertions** |
| New Assembly groups included above | **16 PASS / 0 FAIL, 80 assertions** |
| Planning | **24 PASS / 0 FAIL, 609 assertions** |
| Phase 3C GEOS independent oracle | **552 PASS**, 346 CLEAR / 201 COLLISION / 5 UNKNOWN |
| Phase 3D independent volume oracle | **426 PASS**, 229 CLEAR / 196 COLLISION / 1 UNKNOWN |
| Phase 3D.1 independent native-family oracle | **480 PASS**, 182 CLEAR / 297 COLLISION / 1 UNKNOWN |
| Phase 4A independent process oracle, Debug and Release | **182 PASS each**, 32 CLEAR / 148 COLLISION / 2 UNKNOWN; 103 collisions missed by three-angle sampling |
| Company DIM / placement / holder / static / process | **38 / 9 / 285 / 26 / 11 assertions PASS** |
| Independent company DXF QA | **6,975 curves PASS**, max deviation 4.55e-13 mm |
| Independent company SPLINE QA | **120 instances / 1,272 chords / 21,624 samples PASS**, sampled .000721435 mm <= certified .000970076 mm |
| Native Assembly final Debug, full backend and lifecycle | **227 assertions PASS**, with explicitly NOT RUN opposite-hand mirror feature case |
| Native Assembly final Release, after Min integration fix | **227 assertions PASS**, same explicit native coverage gaps |
| Native six-bend Part Debug / Release | **953 / 953 assertions PASS**, observed native coverage **6/6** |
| Native uniform Part Release | **470 assertions PASS**, observed native coverage **3/3**; optional uniform-only assertion not requested |
| Native legacy Part/Assembly Min + existing UI regression, final Release | **Functional PASS; 1,570 assertions attempted; one inherited layout warning** |

The 242 Phase 4A groups remain. New tests cover 120 occurrences/25 sources,
safe result/shape equivalence separation, every pair/phase slot, serial/2/4
worker determinism, single-flight/LRU/count and accounting limits, staleness,
budgets, cancellation, duplicate indexes and listener isolation. They compare
exact reasons, statuses, clearance and source witnesses against independent
single-Part calls to the inherited engines. The mathematical kernels/policies
are reused, not replaced by a mock.

Two important cache regressions are explicit: different observed slices can
share a volume hash but cannot share a collision certificate; identical setup
payload coordinates with missing review cannot inherit a verified result.
Different approved tool pose/process inputs rerun the affected solvers.
An unresolved subassembly is not a completed empty member inventory.

Native six-bend captures were independently compared with STEP/OpenCascade:
**5,232 points + 12 witnesses per build**, plus **288 points + 6 witnesses**
for the uniform Part, zero skipped. Source Part hashes remained unchanged.
The existing four-component/three-file-config Assembly regression preserves
Default=Đạt, ALT=NG, alternate-config isolation, highlighting, numbering,
section preview and automatic Min update after editing/rebuild. Its first
run found a real integration regression: stricter Batch context rejection had
been assigned to the legacy Min error field. Separating `AssemblyBatchReason`
from the existing Min grouping/error contract fixed it; the final rerun passes
functional checks. No Min measurement or evaluation algorithm was edited.

## Native fixture and source protection

The harness owns a new SOLIDWORKS 2024 API process and refuses to attach to an
existing process. It generates private fixtures from a supplied sheet-metal
seed: 120 leaf occurrences, two nested subassembly branches, 23 private files
and 25 file/configuration identities (two alternate configurations). Native
metadata finds normal sheet-metal bodies; broken references are distinguished
from live dependencies. All setup/save/rebuild operations apply only to test
copies before read-only checks. Original seed bytes remain unchanged.

- All scope: 120 instances, 25 native captures, 25 optional B-rep appends;
  each unique Min context has `GeometryReadCount=1`.
- Warm: zero native geometry recaptures/appends; 25 capture hits. Captured
  geometry remains reusable after a canceled generation only after a full
  native revalidation. Native `WrongThreadCount=0`.
- Selected scope: exactly 10 instances; the OS/API enumeration selected four
  source/configuration identities in the final Release run (the final Debug
  run selected six; the first ten enumeration entries need not be identical).
  Selected parent scope expands exactly its selected branch without duplicates:
  70 members in final Release and 50 in final Debug. Child names
  repeat across branches but occurrence identities remain distinct.
- All 120 per-instance static/process outputs match independent legacy
  single-Part engine calls, including missing-data reasons. Original Assembly
  and Part byte hashes, dirty flags, update stamps and active configurations
  match before/after the read-only section.
- Native cancellation, restarting without repeating 25 geometry captures,
  and changing private workbook bytes between initial observation/publication
  are tested. Changed data cannot be published as current.
- Suppressed and actual lightweight states are tested without bulk resolution.
  The simple explicit fixture resolve/restore probe returns to the same
  suppression/stamp/dirty state. It does **not** establish editor/unloaded-doc
  rollback for arbitrary Assemblies; the backend keeps its zero-resolve policy
  and reports the required user action.
- Separate native fixtures verify one non-sheet-metal source, one multibody,
  one virtual definition, **two actual reflected transforms** (negative
  determinant read back from SOLIDWORKS), and **one Assembly-only cut**.
  The cut is not propagated to source Parts; exactly one user body is detected
  and excluded from ordinary source-geometry certification.
- An opposite-hand mirror feature creation attempt returned null in the
  installed API, so **native `IsMirrored` feature provenance is NOT RUN**.
  No mirrored tag is fabricated. The two real reflected-transform cases and
  managed mirrored/derived-context guards pass. Flexible and unresolved-member
  guards have managed coverage; a native flexible/large-design-review fixture
  is not claimed here.

The unchanged atomic Min/COM capture can still occupy STA for approximately
1.96 s in the final Release fixture. Capture is time sliced between Parts,
not preempted within an existing COM traversal. This is an explicit UI-latency
limit; no claim of a 50 ms maximum freeze is made. No COM enters a numeric worker.

## Measured performance

### Native Assembly, actual company tool sources

Missing real setup/material/motion evidence gives UNKNOWN and **zero numeric
kernel queries**. These times measure real inventory/Min capture, source
freshness, gating and caches, **not production collision throughput**.

| Final Release stage | Cold all | Warm all | Selected 10 |
| --- | ---: | ---: | ---: |
| Total wall ms | 44,566.73 | 4,616.48 | 6,731.58 |
| Inventory/revalidation STA ms | 2,089.95 | 1,348.40 | 299.66 |
| Native geometry captures / hits | 25 / 0 | 0 / 25 | 4 / 0 |
| Capture ms | 39,033.45 | .10 | 5,619.60 |
| Envelope compilation ms | 7.88 | 0 | .44 |
| Coordinator precheck ms | 1.78 | .41 | .02 |
| Static accumulated ms | 198.11 | 0 | 7.15 |
| Process accumulated ms | 222.31 | 0 | 6.57 |
| Numeric queries / numeric ms | 0 / 0 | 0 / 0 | 0 / 0 |
| Geometry cache hits / misses | 95 / 25 | 120 / 0 | 6 / 4 |
| Static cache hits / misses | 0 / 120 | 120 / 0 | 0 / 10 |
| Process cache hits / misses | 0 / 120 | 120 / 0 | 0 / 10 |
| Catalog cache hits / misses | 0 / 1 | 1 / 0 | 0 / 1 |
| Backend resolves / resolve ms | 0 / 0 | 0 / 0 | 0 / 0 |
| Peak numeric workers | 4 | 4 | 2 |
| Accounting cache bytes | 34,777,984 | 34,777,984 | 3,032,688 |
| Observed managed heap bytes | 74,929,632 | 169,826,976 | 191,666,240 |

Cold/warm native difference is **39.95 s (9.65×)** in this fixture. The
independent *solver-only* per-instance reference takes 426.17 ms; it reuses
the already captured local geometry and excludes native capture. Comparing
that number with a 44.57 s full native run would be invalid. The inherited
session already grouped Min geometry by file/config; no claim is made that
this phase invented that pre-existing optimization.

Managed heap observations cover the whole test process, including multiple
live sessions, legacy Min rows, transient objects and source-provider data.
They are sampled, not an exact retained-cache heap or SOLIDWORKS working set.
Warm source hashing and full native freshness checks remain real work.

Final Debug also passes 227 assertions: cold/warm **43,476.81 / 4,341.01 ms**,
25/0 captures, warm 25 capture hits and 120 geometry/static/process cache hits;
maximum atomic STA capture 1,880.59 ms. Selected 10 takes 10,037.54 ms with six
native captures. These independent runs reflect different first-ten native
enumeration choices, not a change in requested scope or lost instances.

### Controlled numeric Assembly benchmark

The fixture supplies explicitly controlled synthetic Punch/Holder geometry,
reviewed software setup records and all five process phase states. It does not
replace company CAD or invent physical company approvals. Baseline calls the
existing single-Part static and process engines sequentially for **each
instance**. Every pair status/reason/witness is compared with cold/warm batch.

| Controlled fixture | Sequential ms / queries | Batch cold ms / queries | Warm ms / queries |
| --- | ---: | ---: | ---: |
| 18 tools × 3 bends, one instance | 21,932.03 / 6,786 | 22,101.05 / 6,786 | 5.42 / 0 |
| 18 tools × 6 bends, one instance | 78,185.46 / 13,572 | 78,724.40 / 13,572 | 7.56 / 0 |
| 120 instances / 25 unique one-bend programs | 224,522.80 / 205,200 | 15,028.11 / 42,750 | 22.45 / 0 |

The final repeated-source fixture measured **14.94× cold speedup**, saving **209.49 s**
against the stated per-instance reference. It has 25 cold source jobs and 25
static/process warm hits, maximum four numeric workers. Actual cold
static results: 300 CLEAR / 150 COLLISION; complete controlled process results:
150 CLEAR / 300 COLLISION, identical to the reference after occurrence mapping.

| Final controlled numeric stage | 18×3 cold | 18×6 cold | 120/25 cold | 120/25 warm |
| --- | ---: | ---: | ---: | ---: |
| Envelope ms | .06 | .0024 | .0272 | .0265 |
| Static ms | 124.71 | 292.15 | 1,014.02 | 0 |
| Process ms | 21,898.26 | 78,222.54 | 53,497.73 | 0 |
| Numeric kernel ms | 18,336.71 | 66,826.88 | 38,178.57 | 0 |
| Geometry hits / misses | 0 / 1 | 0 / 1 | 0 / 25 | 0 / 25 |
| Static hits / misses | 0 / 1 | 0 / 1 | 0 / 25 | 25 / 0 |
| Process hits / misses | 0 / 1 | 0 / 1 | 0 / 25 | 25 / 0 |
| Accounting cache bytes | 2,370,544 | 4,724,320 | 17,812,848 | 17,812,848 |
| Sampled managed heap bytes | 8,113,396 | 14,917,100 | 54,821,132 | 65,695,788 |
| Peak numeric workers | 1 | 1 | 4 | 4 |

This benchmark starts from managed DTOs, so inventory, native capture and
resolve time/count are zero. Its final 25-source working set exceeds the
geometry cache's accounting budget; FIFO revisiting causes 25 geometry misses
and inexpensive recompiles on the warm pass. Static/process certificates still
hit all 25 entries with zero numeric queries. No geometry hit is claimed for
that pass. One-Part warm geometry/static/process hits are 1/1/1. The benchmark
finishes **12 assertions PASS**, including exact independent result comparison.

For one Part, the coordinator schedules one single-Part job and does not
parallelize that job internally; final cold overhead is about 0.7–0.8%, not a speedup.
The original 4A benchmark covered only the forming segment, whereas this one
supplies all phases. Neither timings nor query counts should be conflated.
Accumulated per-worker stage timings can exceed total batch wall time.

## Data limitations and remaining validation

All 18 actual IDs remain in every known bend's output. Company data still has
17 valid closed Punch regions, one source-open KIK, 14 Holder reference
inventories and four unresolved mappings (AMD-STD-004/008/117/452). All 120
SPLINE instances parse through the inherited backend. There are no reviewed
actual material roles, physical mounts, finite axial setup or process/motion
inputs. Thus Punch/Holder/process remain **UNKNOWN**, `CHUA_XAC_MINH`, no winner.
See the baseline's [18-tool table](Validation-Phase4A.md#all-18-actual-company-tool-statuses);
this phase changes no source geometry, mapping approvals or minimum rules.

Native opposite-hand feature, flexible and large-design-review creation gaps
remain explicitly unverified; reflected native transforms do not prove the
opposite-hand feature case.
The inherited UI stress warning remains 57 px text in a 53 px material row at
1,000 px; no UI repair is included or falsely called clean layout PASS.

## Reproduction and development failures

Use MSBuild .NET Framework 4.8 / C# 7.3. Product builds use private output and
disable `KillSolidWorksOnBuild`, PreBuildEvent and PostBuildEvent. No installed
DLL is replaced and no user SOLIDWORKS process is stopped. Test dependencies
(GEOS/Shapely, ezdxf, OpenCascade) are QA-only outside the repository.

- `Tests/BendToolProfiles/ToolProfiles.Tests.csproj`: normal full suite;
  `--assembly-tests`; `--assembly-benchmark <private-output>`.
- Existing `--collision-oracle`, `--envelope-oracle`, `--native-family-oracle`,
  `--process-oracle` exports and their `compare_*.py` independent scripts.
- Existing actual-company integration modes and `Tests/BendPlanning` suite.
- `NativeAssemblyBatch.Validation.csproj`: DLL, private output, sheet-metal
  seed, parameter name, Assembly template and minimum workbook arguments.
  Company source config is supplied by the existing environment/settings API.
  All generated CAD, JSON, evidence and logs stay local/private.

Development failures were retained locally: initial sandbox COM activation
denied access; helper compilation/argument errors; a first guard rescanned all
motion graphs at each kernel callback, slowed cold runs and produced one
time-budget comparison difference. The final cheap-lease/full-barrier split
restored deterministic comparisons. Initial native grouping rejected all
broken references; explicit native status inspection corrected that overly
broad rejection. Fixture creation needed correct Assembly activation, private
virtual source isolation, direct sketch database insertion and explicit cut
scope. These harness failures are not relabeled as product PASS. Some owned
test processes needed verified-PID/private-document cleanup after failed API
cases; user processes/documents were not terminated.

One native rerun request was not executed because automatic approval review
had reached its service usage limit. After user continuation it was retried
through the same approval mechanism and passed; this was not a native test
failure and no approval check was bypassed.

No CAD/Excel/STEP, proprietary coordinates/paths/hashes, signed approvals,
private keys, screenshots or dependencies are committed. Source protection
checks compare the original Min `Capture` method byte-for-byte modulo line
endings and confirm no WinForms/Designer/event-handler/Planning changes.

## Changed file inventory

23 source/project/documentation files are included:

- Assembly backend: `Commands/BendAssembly/AssemblyBatchCoordinator.cs`,
  `AssemblyBatchModels.cs`, `AssemblyCacheKeys.cs`,
  `AssemblyCollisionCertificates.cs`, `AssemblyManagedCache.cs`.
- Native session bridge: `Commands/BendCheckSession.cs`,
  `Commands/BendCheckSession.AssemblyInventory.cs`,
  `Commands/BendCheckSession.AssemblyBatch.cs`.
- Existing engine integration/indexes and timing:
  `Commands/BendEnvelope/CollisionBatchWorker.cs`,
  `Commands/BendProcess/ProcessBatchWorker.cs`,
  `Commands/BendCollision/CollisionEngine2D.cs`,
  `Commands/BendCollision/CollisionTimingScope.cs`,
  `Commands/BendToolProfiles/ToolProfileLibrarySession.cs`.
  The 2D engine's only change is a timing wrapper around its unchanged kernel.
- Project includes: `ADDIN.csproj`,
  `Tests/BendToolProfiles/ToolProfiles.Tests.csproj`.
- Tests/harness: `Tests/BendToolProfiles/ToolProfileTests.cs`,
  `AssemblyBatchTests.cs`, `AssemblyBatchBenchmark.cs`,
  `NativeAssemblyBatch.Validation.csproj`, `NativeAssemblyBatchValidation.cs`,
  `NativeAssemblyBatchEdges.cs` (all under `Tests/BendToolProfiles/`).
- Documentation: `Docs/BendCheck/Phase4A1-AssemblyBatchContract.md`,
  `Docs/BendCheck/Validation-Phase4A1.md`.

Local build/test evidence is reported above. No GitHub Actions success is
claimed from these local runs; remote CI status is reported separately in
the Draft PR handoff.

## Follow-up code-review fix: mixed valid/unsupported occurrences (pending Windows rerun)

A subsequent PR #24 review found that `BendCheckItem.AssemblyBatchReason` could retain the first mirrored/flexible occurrence's
error on a **shared file/config Check Min group**. The original `AssemblyNativeBatchOperation.Pump` then skipped native capture
and propagated that single reason to every sibling. This could incorrectly turn eligible ordinary components into UNKNOWN.

The follow-up commit changes only the Assembly inventory/batch partitioning and adds managed/native regressions:
- Check Min retains the original `BendCheckItem` grouping, quantity and measurement semantics.
- Eligibility is evaluated per `AssemblyOccurrence` (including mirror/modified/flexible/virtual/source identity).
- A group containing eligible members is captured once. Valid siblings are analyzed together only with the original proof rules;
  every blocked sibling receives its own source-reasoned UNKNOWN rows for each **known** bend × 18 tools, without setup evidence.
- Regression covers both traversal orders, two valid + four blocked siblings sharing file/config, independent collision witnesses,
  and a native reflected-transform + ordinary same-source fixture.
- This change is **not yet re-run in Windows/SOLIDWORKS by this review**. Existing PASS/benchmark numbers above belong to the
  earlier PR #24 commit and must not be presented as post-fix test results. Re-run Debug/Release x64, managed `--assembly-tests`,
  native assembly fixture (including mixed-identity case), original Check Min/Planning, and controlled numeric benchmark before merge.
