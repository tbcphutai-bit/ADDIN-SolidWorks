# Phase 4A.1 follow-up — mixed occurrence isolation

Date: 2026-10-19. PR [#24](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/24),
branch `feature/check-chan-phase4a1-assembly-batch`.

The branch was fetched and fast-forwarded to ChatGPT's fix
`5f1e468f2d7cfae6e240f65a80865a506c83f5d7`. Its PR conversation comment was
read before changes. The partitioning fix is retained; no reset, revert, merge
or edit of the parent PR branches was performed. Historical numbers in
`Validation-Phase4A1.md` describe `73a4566`, not this follow-up.

## Review and changes

The original fix correctly separates capturable normal occurrences from
blocked siblings while retaining the existing Min file/configuration item.
Its new managed test passes both enumeration orders with two normal and four
unsupported occurrences, actual numeric queries, all 18 candidates and exact
independent single-Part status/reason/witness equivalence.

Additional defects found during this Windows run:

1. **Direct managed API bypass.** A caller supplying `AssemblySourceInput`
   directly could bypass native partitioning. Virtual, uncertified or explicitly
   rejected occurrences could enter the solver. A new regression first failed;
   the coordinator now applies the same eligibility partition at admission and
   `CanShare` uses that eligibility contract. Normal siblings still perform
   numeric work; rejected siblings retain independent UNKNOWN slots.
2. **Unsupported virtual side file poisoned publication.** Native testing twice
   recorded a virtual component's temporary side file changing while SOLIDWORKS
   switched activation. It invalidated the whole batch although the virtual
   occurrence was already unsupported and never supplied certified geometry.
   File-proof dependencies now include eligible captured source files, the
   Assembly and the minimum workbook. Required source hashes, host/config/pose
   stamps, tool/review evidence and cancellation guards remain in place.
   Unsupported occurrences remain UNKNOWN; no unverified geometry is omitted
   from a CLEAR proof.
3. **Selected flexible descendants lost ancestry.** All scope recorded a Part
   below a real flexible subassembly as unsupported, but selecting the child
   directly initialized traversal without its parent's flexible flag. Native
   inventory exposed the mismatch. Traversal and freshness auditing now inspect
   actual ancestor solving state. The native assertion compares blocked reasons
   in All and Selected scope, rather than only checking selected quantity.

No Check Min measurement/evaluation, WinForms/Designer/event handler, other
command, lower-die logic or mathematical collision kernel is changed. The
original Min `Capture` body is compared with the Phase 4A baseline. Manufacturing
status stays `CHUA_XAC_MINH`, winner is null.

## Observed checks

| Run | Observed result |
| --- | --- |
| Unmodified `5f1e468` product Debug/Release x64 | PASS, 0 errors; 16 inherited warnings each |
| Unmodified `5f1e468` full managed Debug/Release | 259 PASS / 0 FAIL, 8,822 assertions each |
| Unmodified `5f1e468` Assembly-only Debug/Release | 17 PASS / 0 FAIL, 103 assertions each |
| Unmodified `5f1e468` native 120/25 + original mixed fixture | 235 assertions PASS |
| Added direct-admission regression before repair | 17 groups PASS / 1 FAIL; failure retained locally |
| Assembly-only after repair | 18 PASS / 0 FAIL, 112 assertions |
| Full managed after repair | 260 PASS / 0 FAIL, 8,831 assertions in Debug and optimized Release |
| Explicit x64 managed rerun, Debug/Release | 260 PASS / 0 FAIL, 8,831 assertions each; Assembly-only 18 PASS / 112 assertions each |
| Product after native source/ancestry fixes | Debug/x64 and Release/x64 PASS, 0 errors; 16 inherited warnings each |
| Phase 3C independent GEOS oracle | 552 PASS: 346 CLEAR / 201 COLLISION / 5 UNKNOWN |
| Phase 3D independent volume oracle | 426 PASS: 229 CLEAR / 196 COLLISION / 1 UNKNOWN |
| Phase 3D.1 independent native-family oracle | 480 PASS: 182 CLEAR / 297 COLLISION / 1 UNKNOWN |
| Phase 4A independent continuous-motion oracle | 182 PASS: 32 CLEAR / 148 COLLISION / 2 UNKNOWN; 103 missed by three-angle sampling |
| Bend Planning | 24 PASS / 0 FAIL, 609 assertions |
| Company placement / Holder / static / process | 9 / 285 / 26 / 11 assertions PASS; 18 tools retained, no approved company process fabricated |
| Native six-bend Part Debug/Release | 953 / 953 assertions PASS, original input hashes unchanged |
| Canonical native uniform Part Release | 471 assertions PASS, original input hash unchanged |
| Final native mixed All/Selected, including ancestry assertion | 153 assertions PASS; 2 normal, 2 reflected, 1 cut, 1 real flexible descendant; Min quantity 5, geometry reads 1 |
| Final Release native 120/25 + mixed + selected LW + lifecycle | 283 assertions PASS, including exact All/Selected blocked reasons after ancestry repair |
| Check Min backend Part/Assembly core | 130 assertions PASS; selected/all, Default/ALT-NG, quantity, details/highlighting and protected working-copy cleanup |

Native STEP/OpenCascade checks compare 5,232 points and 12 witnesses for each
six-bend run, and 288 points plus six witnesses for the uniform fixture. Native
fixtures are supplied local copies from the previous validation, not generated
replacement geometry for the company tools.

The final native run preserves all original seed bytes and all fixture
Part/Assembly bytes, dirty flags, update stamps and active configurations across
the read-only section. All 120 per-instance static/process outputs match the
independent single-Part calls. It captures 25 Min contexts once, appends 25 native
topologies, and reuses all 25 after cancel/restart with zero worker COM.

| Final Release native metric | Cold all | Warm all | Selected 10 |
| --- | ---: | ---: | ---: |
| Wall ms | 79,427.96 | 7,744.49 | 12,981.31 |
| Inventory/audit ms | 3,852.56 | 2,065.57 | 487.89 |
| Captures / capture hits | 25 / 0 | 0 / 25 | 4 / 0 |
| Capture ms | 69,784.63 | .30 | 10,762.54 |
| Envelope ms | 37.61 | 0 | .94 |
| Static / process ms | 394.98 / 583.60 | 0 / 0 | 11.79 / 23.29 |
| Geometry hits / misses | 95 / 25 | 120 / 0 | 6 / 4 |
| Static hits / misses | 0 / 120 | 120 / 0 | 0 / 10 |
| Process hits / misses | 0 / 120 | 120 / 0 | 0 / 10 |
| Catalog hits / misses | 0 / 1 | 1 / 0 | 0 / 1 |
| Numeric kernel queries | 0 | 0 | 0 |
| Peak workers | 4 | 4 | 2 |
| Accounting cache bytes | 34,777,984 | 34,777,984 | 3,032,688 |
| Sampled managed heap bytes | 157,194,552 | 165,345,880 | 201,572,672 |

The previous `73a4566` native Release run was 44,566.73 / 4,616.48 ms cold/warm;
the unmodified `5f1e468` Debug run in this follow-up was 46,533.69 / 4,692.58 ms.
The current native run is slower, not a claimed optimization. Ancestry auditing
adds real COM work, but timing conditions and process startup also changed; no
isolated causal attribution is made. Capture count remains 25/0. The final
maximum atomic STA capture is 3,583.42 ms, so a small fixed UI-latency bound is
still not proven. These runs perform zero numeric queries and are **not proof
of collision-computation speedup**. Independent per-instance solver-only gating
is 1,253.68 ms after capture and cannot be compared with full native capture time.

The final selected native LW batch performs zero capture/resolution, returns
UNKNOWN and retains actual LW state 1. The explicit fixture restore probe
preserves its prior suppression/stamp/dirty values. The workbook-before-publish
mutation and cancel/restart checks also pass after the file-dependency repair.

## Numeric benchmark

These measurements use complete controlled geometry/process inputs, not company
mount approvals. The explicit-x64 120/25 run completes four assertions, including
exact batch-versus-independent status/reason/witness comparison. It reports
32 logical processors and at most four numeric workers.

| 120 occurrences / 25 geometries | Previous `73a4566` | Follow-up |
| --- | ---: | ---: |
| Independent sequential wall ms | 224,522.80 | 279,657.82 |
| Independent numeric queries | 205,200 | 205,200 |
| Batch cold wall ms | 15,028.11 | 10,103.82 |
| Cold numeric queries | 42,750 | 42,750 |
| Peak cold workers | 4 | 4 |
| Batch warm wall ms | 22.45 | 18.74 |
| Warm numeric queries | 0 | 0 |
| Cold static/process misses | 25 / 25 | 25 / 25 |
| Warm static/process hits | 25 / 25 | 25 / 25 |

The measured within-run cold ratio is 27.68x (previous run 14.94x). Both runs
perform the same numeric query counts; differing wall times are observations,
not an isolated performance gain attributable to this eligibility repair. Warm
reuse restores previously computed certificates. Native capture is outside this
managed fixture. Its final accounting cache is 17,812,848 bytes, sampled cold/warm
managed heap 68,671,808 / 53,307,672 bytes. The geometry working set exceeds its
16 MiB budget: warm geometry hits/misses are 2/23, while all 25 static and all
25 process certificates still hit. No claim of 25 geometry hits is made.

In the explicit-x64 combined benchmark, the three-bend case completed exact
numeric comparison: independent 21,548.01 ms / 6,786 queries, cold 20,081.25 ms /
6,786 queries, warm 3.68 ms / zero queries (one worker). The subsequent six-bend
independent reference exceeded the unchanged 120-second process-job budget:
120,112.23 ms, 9,436 queries and `PROCESS_JOB_TIME_BUDGET_EXCEEDED`. That combined
run is **FAIL**, not a complete six-bend benchmark or a new CLEAR result. Reason
aggregation also includes successful-pair reasons; the budget code is the
observed UNKNOWN cause. An isolated six-bend rerun is recorded separately below.

The isolated explicit-x64 six-bend retry also **FAILS** its complete-reference
requirement: 120,193.82 ms, 7,551 queries, the same
`PROCESS_JOB_TIME_BUDGET_EXCEEDED`. The production budget and UNKNOWN contract
are unchanged. A complete six-bend timing remains unavailable in this run;
improved budget/performance investigation is outstanding. The successful native
six-bend **geometry coverage** test above does not prove completion of this
different, supplied continuous-motion benchmark.

## Check Min regression scope

The native core regression completes 130 assertions. The retained legacy Min
grouping produces Default quantity 2 / `Đạt`, alternate configuration quantity
1 / `NG`, and the five-bend Part quantity 1 / `Đạt`. It exercises selected/all,
one geometry read per context, real placement/static/process preparation,
detail rendering/highlight/numbering, and alternate-configuration working-copy
cleanup. Host dirty flag, update stamp and configuration remain unchanged.

This is explicitly `CORE_ONLY_NO_UI_STRESS`. The long legacy UI stress run did
not complete; it is not included in the 130 PASS assertions. Final full native
Assembly after the ancestry fix was run in Release. The earlier concurrent
Debug run timed out; final full native Debug after this fix remains NOT RUN.

## Native evidence and honest exclusions

The new fixture uses two normal occurrences, two actual negative-determinant
transforms, one Assembly-only cut and a real flexible subassembly descendant
sharing the normal Part/configuration. Its Min group contains five occurrences
(two normal, two reflected, one flexible); the assembly-cut item retains the
legacy isolated Min error/grouping. Batch must capture the eligible geometry
once, retain the normal rows, and block other occurrences independently.

Every captured bend has 18 static and 18 process candidates. Reflected/flexible
siblings can retain the known bend IDs from the captured group, without setup
evidence. An unmeasured cut/lightweight instance reports unknown bend inventory;
no bend count or certificate is invented. Suppressed occurrences remain in
inventory accounting and are skipped for computation.

The installed API returns resolved state after requesting lightweight in the
flexible fixture. That combined case is explicitly NOT RUN as lightweight; it
is not relabeled or assigned a synthetic flag. The standalone selected-LW batch
uses an actual native LW state, requires zero capture/resolve and UNKNOWN, and
checks the state remains unchanged. Native opposite-hand Mirror Feature creation
still returns null; its `IsMirrored` provenance remains NOT RUN. Real reflected
transforms and flexible ancestry are separate native evidence.

## Development failures retained

- COM activation can bind an existing user process. Initial attempts correctly
  refused ownership. The test broker starts a hidden executable and accepts
  only its exact PID through ROT. All API calls remain on STA. Server stdout
  is drained independently so inherited pipes do not retain a finished harness.
- An experimental `-Embedding` launch did not register the required owned ROT
  object and was not counted as a test run; that mode was removed.
- Two virtual-side-file staleness failures led to the dependency repair above;
  the stale guard itself was not disabled.
- Lightweight fixture preparation was corrected to use actual readback. A
  successful setter return is not proof that SW retained lightweight state.
- One nominal uniform run supplied the wrong local Part and correctly failed
  the strict uniform predicate. The canonical prior uniform fixture was then
  used, with the predicate still enabled: 471 assertions PASS.
- An early native run reported 280 PASS but lacked the Selected flexible-reason
  comparison. Its inventory exposed the defect; it is not proof of the final
  ancestry fix. A concurrent Debug run exceeded the native time budget and is
  retained as FAIL, not silently reported as completed.
- A numeric benchmark attempt returned UNKNOWN in its independent reference
  and was marked FAIL. Its original reason detail was not retained, so no exact
  cause is claimed. The benchmark now records incomplete-reference reasons;
  incomplete results cannot be used as performance PASS.
- A long UI regression harness was stopped. Its SOLIDWORKS instance contained
  referenced documents outside its fixture directory, so automatic cleanup
  refused to terminate that SW process. Only the verified owned harness exe
  was stopped; the SW instance and documents were left untouched.

No company CAD/Excel/STEP, private paths/hashes/coordinates, review artifacts,
keys, captures, screenshots, dependencies or local logs are committed.

## Reproduction

Use .NET Framework 4.8 / C# 7.3. Product builds disable kill/pre/post actions
and use private output. The pure test project explicitly targets x64.

- `ToolProfiles.Tests.exe --assembly-tests`, then the full executable in Debug
  and optimized TRACE-only Release.
- Existing independent oracle exports and `compare_*.py` scripts; QA dependencies
  remain outside the repository.
- `NativeAssemblyBatch.Validation.csproj`: DLL, private output, local seed,
  dimension name, Assembly template and minimum workbook. Optional
  `ADDIN_ASSEMBLY_EDGE_ONLY=1` also executes mixed checking, not inventory alone.
- `NativeToolLibrary.Validation.csproj`: existing native Part and STEP oracle
  arguments, using the same PID-specific test broker.
- `ToolProfiles.Tests.exe --assembly-benchmark <private-output>`: controlled
  synthetic geometry and supplied complete process states; no company approvals
  are invented. Run numeric timing independently of heavy native test load.
  An optional third argument selects `Numeric18x3`, `Numeric18x6` or
  `Numeric120x25`; incomplete references record reason counts and fail.

## Changed files

- `Commands/BendAssembly/AssemblyBatchCoordinator.cs`
- `Commands/BendAssembly/AssemblyBatchModels.cs`
- `Commands/BendCheckSession.AssemblyBatch.cs`
- `Commands/BendCheckSession.AssemblyInventory.cs`
- `Docs/BendCheck/Phase4A1-AssemblyBatchContract.md`
- `Docs/BendCheck/Validation-Phase4A1.md`
- `Docs/BendCheck/Validation-Phase4A1-MixedIsolation.md`
- `Tests/BendToolProfiles/AssemblyBatchBenchmark.cs`
- `Tests/BendToolProfiles/AssemblyBatchTests.cs`
- `Tests/BendToolProfiles/NativeAssemblyBatch.Validation.csproj`
- `Tests/BendToolProfiles/NativeAssemblyBatchEdges.cs`
- `Tests/BendToolProfiles/NativeAssemblyBatchValidation.cs`
- `Tests/BendToolProfiles/NativeAssemblyMixedTests.cs`
- `Tests/BendToolProfiles/NativeTestProcess.cs`
- `Tests/BendToolProfiles/NativeToolLibrary.Validation.csproj`
- `Tests/BendToolProfiles/NativeToolLibraryValidation.cs`
- `Tests/BendToolProfiles/ToolProfiles.Tests.csproj`

API references: [SOLIDWORKS solving state](https://help.solidworks.com/2020/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IComponent2~Solving.html),
[fixture-only configuration/solving setter](https://help.solidworks.com/2021/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.iassemblydoc~compconfigproperties5.html).
