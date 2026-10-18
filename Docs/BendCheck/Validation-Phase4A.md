# Phase 4A validation — 2026-10-18

Issue [#21](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/21).
Baseline PR #19: `949dd92f6cf7dd2fd1f4e8e9333920457900748f`.
Branch `feature/check-chan-phase4a-motion-states`; Draft target
`feature/check-chan-phase3d1-native-coverage`. No older PR/main branch is edited
or merged. All results below are observed local Windows runs, not invented CI.

The implementation adds an executable continuous-motion solver, complete
controlled one-bend and two-joint fixtures, a narrowly proved intended-contact
rule, and a bounded managed 18-candidate batch. See the
[geometry, evidence, error and API contract](Phase4A-ProcessMotionContract.md).

## Build and managed tests

| Check | Final observed result |
| --- | --- |
| Product Debug/x64 | PASS, zero errors, 16 inherited CS0219/CS0649 warnings |
| Product Release/x64 | PASS, zero errors, same 16 warnings |
| Managed DEBUG, unoptimized | **242 PASS / 0 FAIL, 8,719 assertions** |
| Managed Release, optimized, TRACE only | **242 PASS / 0 FAIL, 8,719 assertions** |
| Bend Planning regression | **24 PASS / 0 FAIL, 609 assertions** |
| Phase 3C GEOS oracle | **552 PASS:** 346 CLEAR / 201 COLLISION / 5 UNKNOWN |
| Phase 3D axial-volume oracle | **426 PASS:** 229 CLEAR / 196 COLLISION / 1 UNKNOWN |
| Phase 3D.1 native-family oracle | **480 PASS:** 182 CLEAR / 297 COLLISION / 1 UNKNOWN |
| New process closed-form oracle, Debug and Release | **182 PASS each:** 32 CLEAR / 148 COLLISION / 2 UNKNOWN |
| Company DIM / placement / holder / static collision / process | **38 / 9 / 285 / 26 / 11 assertions PASS** |
| Independent company DXF QA | **6,975 curves PASS**, maximum observed coordinate/radius/midpoint deviation 4.55e-13 mm |
| Independent holder SPLINE QA | **120 instances / 1,272 chords / 21,624 samples PASS**; maximum sampled deviation .000721435 mm <= certified .000970076 mm |

The 222 groups / 8,130 assertions of Phase 3D.1 are preserved. Twenty new groups
add 589 assertions, including executable whole-motion results, independent
Punch/Holder outcomes, constraints, contact, evidence, budgets and worker tests.
Test-only Python backends are Shapely 2.2.0 / GEOS 3.14.1, ezdxf 1.4.5 and
OpenCascade 7.8.1.1; the add-in does not depend on/load them.

### New process cases exercised

- Explicit fixed panel, moving panel and bounded bend zone, with certified
  whole-angle separation and source/parameter-bound collision witnesses.
- A disk obstacle at an interior angle: **start, midpoint and final all CLEAR**,
  yet adaptive continuous checking proves COLLISION elsewhere.
- Explicit load/pre-bend/form/end/unload path, feed/retract-only interference,
  finite axial translation, cap contact and disjoint tool segments.
- Three-panel/two-joint fixture, same final shape under two supplied orders:
  one has a middle-step collision, the other has separated modeled steps.
  No order search or winner selection is performed.
- Certified bounded joint region vs missing joint bound; an enclosure-only
  overlap cannot supply a material collision witness.
- Approved stationary planar Punch surface contact => EXPECTED_CONTACT;
  missing/narrow/wrong-phase zone => UNKNOWN, real penetration => COLLISION.
  Holder contact is never exempted.
- ARC tangency; a real bounded rational-SPLINE holder backend, including its
  uncertainty band; source uncertainty growth cannot create CLEAR.
- Adding obstacle material retains the existing witnessed collision in the
  controlled test; no material subset is silently dropped.
- Reuse of native transverse-cylinder source geometry with an explicitly
  supplied rigid motion, without reconstructing intermediate CAD.
- Wrong frame, finished-CAD role, unmodeled panel join, changed finite tool
  occupancy at a transition, unverified review/physical seating, stale model,
  stale process and stale Holder source all fail closed.
- Cancellation, pair time/depth/interval and real batch kernel-call budgets;
  progress exceptions, disposal, supersession, stable serial/parallel results.
- All 18 IDs × 3/6 bends: 54/108 candidates, both pair outcomes retained and
  missing phases represented explicitly. Four missing phases in the partial
  synthetic forming fixture never become a full-process clearance.

### Independent process oracle

`compare_process_motion.py` imports no production solver or geometric kernel.
It derives the exact whole-angle distance between a rotated rectangular panel
and a finite circular obstacle in a deliberately constrained analytic family.
It checks every CLEAR against the exact minimum gap and every COLLISION witness
against the actual rotated panel, obstacle disk and finite axial overlap.

Both configurations have zero false CLEAR, zero false COLLISION and two
intentionally unresolved tangencies. **103 of the 182 cases** have a real
collision omitted by sampling only the start/midpoint/end angles. These are
whole-angle analytic comparisons, not a denser version of the production angle
sampling. Existing axial/native oracles independently retain the continuous
source-volume checks.

## Native SOLIDWORKS Part and Assembly regression

Read-only API tests used new owned SOLIDWORKS 2024 processes and private copies.
Original Part hashes are unchanged. Existing Check Min, dimension/highlight,
rebuild/configuration refresh, grouping and Planning checks pass.

| Run | Assertions / result | Native sheet coverage | Process candidates |
| --- | --- | --- | --- |
| Six-bend Part Debug | **953 PASS** | 6/6, 11 certified intervals each | 108 UNKNOWN, 1,080 pair/phase results |
| Six-bend Part Release | **953 PASS** | 6/6, 11 certified intervals each | 108 UNKNOWN, 1,080 pair/phase results |
| Uniform Part Release | **471 PASS** | 3/3 Uniform2DProven | 54 UNKNOWN, 540 pair/phase results |
| Assembly Release | **Functional PASS, 1,570 assertions attempted** | Existing file/config sources retained | 90 / 54 / 54 UNKNOWN per component group |

The six-bend coverage inherited from Phase 3D.1 remains unchanged:

| Feature | Baseline | Phase 4A | Intervals |
| --- | --- | --- | ---: |
| 805 | AxialSlicesWithCertifiedCoverage | same | 11 |
| 811 | AxialSlicesWithCertifiedCoverage | same | 11 |
| 867 | AxialSlicesWithCertifiedCoverage | same | 11 |
| 873 | AxialSlicesWithCertifiedCoverage | same | 11 |
| 890 | AxialSlicesWithCertifiedCoverage | same | 11 |
| 906 | AxialSlicesWithCertifiedCoverage | same | 11 |

For each six-bend run, independent STEP/OpenCascade B-rep classification checks
**5,232/5,232 material/void points, zero skipped and 12 collision witnesses**.
The uniform run adds 288/288 points and six witnesses. Maximum six-bend native
extrema/frame discrepancy is 1.78e-15 mm. All controlled finite collision/clear
probes pass. This is regression QA of observed native geometry, not reconstruction
of a real forming path; production continuity still comes from analytic bounds.

Min `GeometryReadCount` stays **1** per file/configuration; the optional B-rep
append is **once** per source snapshot. Process batches add **zero** COM reads;
native `WrongThreadCount=0`. Managed worker thread IDs differ from STA capture.
Assembly preserves selected/all scope, four instances grouped into three
file/configuration entries, moved/rotated instance highlighting, quantity,
alternate NG configuration, sketch/rebuild refresh and source-only refresh.

### Existing warnings and development failures

The inherited Assembly layout stress warning remains: at 1,000 px, a wrapped
material cell measures **57 px for a 53 px row**. Therefore 1,570 attempted
assertions are **not** reported as clean layout PASS. UI edits are outside scope.

The historical native initial-pass failure is not reproduced: the current
Part gate records `Current=true`, `Done=true`, `MinimumText=Đạt`, `Error=null`
before the controlled sketch modification. The previous Phase 3D.1 object-key
ordering test false positive remains fixed by test-only canonical comparison.
Earlier failures without captured diagnostics are not retroactively declared
solved by this phase.

During development, an initial 30 s serial/parallel comparison failed before
constant-pair caching; that run did not retain individual timeout reasons.
The deterministic test now sets 120 s explicitly, and all final comparisons
pass. Observed Debug serial 6-bend time is 29.34 s, close to the original job
budget; this does not establish the exact cause of the unrecorded first
difference. Actual budget exhaustion is independently tested and returns
UNKNOWN. Default production limits are still finite and conservative.

A private Assembly QA helper initially failed compilation (CS0136, a local
`rows` variable shadowed the existing helper variable); it was renamed. Final
helper and product builds pass. No source/UI repair was made for this harness
error. The old unsupported native ray API is not used or reintroduced.

## Performance observations

These are local wall-clock observations, not a machine/operator throughput
claim. Capture, static envelope work and modeled process work are separate.

| Run | STA B-rep append | Existing static managed batch | New process batch |
| --- | ---: | ---: | ---: |
| Six-bend Debug | 629.46 ms once | 59.81 ms / 108 rows | 3.11 ms / 108 UNKNOWN, **0 numeric queries** |
| Six-bend Release | 626.92 ms once | 95.34 ms / 108 rows | 11.64 ms / 108 UNKNOWN, **0 numeric queries** |
| Uniform Release | 357.45 ms once | 42.51 ms / 54 rows | 6.26 ms / 54 UNKNOWN, **0 numeric queries** |

Actual-tool UNKNOWN timings measure gating/DTO work only. They do not benchmark
fully verified 18-tool motion collision.

Controlled 18-tool forming-segment fixtures do execute real geometry:

| Configuration | Bends / rows | Worker, 2 threads | Serial | Kernel calls | Refinements |
| --- | --- | ---: | ---: | ---: | ---: |
| Debug | 3 / 54 | 4,594.23 ms | 8,763.15 ms | 2,268 | 180 |
| Debug | 6 / 108 | 15,309.70 ms | 29,344.69 ms | 4,536 | 360 |
| optimized Release | 3 / 54 | 2,983.89 ms | 5,725.18 ms | 2,268 | 180 |
| optimized Release | 6 / 108 | 9,979.29 ms | 18,969.01 ms | 4,536 | 360 |

The caller returns in 0.008–0.060 ms in these fixture runs. Each forming set has
six Punch-hit, six Holder-hit and six separated tools per bend; unknown
load/pre/end/unload entries remain unresolved. Geometry is synthetic; company
CAD is not substituted by these test profiles. No UI path is changed to invoke
the new worker automatically.

## All 18 actual company tool statuses

Every row below has missing reviewed motion/stage/contact/physical setup and
finite axial evidence; **Punch=UNKNOWN, Holder=UNKNOWN, CHUA_XAC_MINH**, no winner.
The reference column means source selection only, not reviewed material or fit.

| Tool | Punch region | Holder reference identified | Process status |
| --- | --- | --- | --- |
| AMD-STD-003 | valid closed | yes | UNKNOWN |
| AMD-STD-004 | valid closed | unresolved | UNKNOWN |
| AMD-STD-0048 | valid closed | yes | UNKNOWN |
| AMD-STD-0049 | valid closed | yes | UNKNOWN |
| AMD-STD-008 | valid closed | unresolved | UNKNOWN |
| AMD-STD-045 | valid closed | yes | UNKNOWN |
| AMD-STD-047 | valid closed | yes | UNKNOWN |
| AMD-STD-103 | valid closed | yes | UNKNOWN |
| AMD-STD-109 | valid closed | yes | UNKNOWN |
| AMD-STD-117 | valid closed | unresolved | UNKNOWN |
| AMD-STD-200 | valid closed | yes | UNKNOWN |
| AMD-STD-202 | valid closed | yes | UNKNOWN |
| AMD-STD-210 | valid closed | yes | UNKNOWN |
| AMD-STD-452 | valid closed | unresolved | UNKNOWN |
| AMD-STD-453 | valid closed | yes | UNKNOWN |
| AMD-202662 | valid closed | yes | UNKNOWN |
| AMD-204662 | valid closed | yes | UNKNOWN |
| KIK-7181328001 | source open | yes | UNKNOWN |

No source-open DXF is repaired. Four mappings remain unresolved. Fourteen
selected Holder inventories, including all 120 SPLINE instances, parse; zero
reviewed material-region selections, physical pair approvals, production poses
or finite axial setup records are fabricated. Source Excel/group data remains
unchanged; current saved local revisions are not claimed identical to absent
files with other suffixes.

## Reproduction

Use private paths for all outputs and company sources. Product builds disable
pre/post steps to avoid killing SOLIDWORKS or replacing the installed add-in:

```powershell
& $MSBuild ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 `
  /p:UseSharedCompilation=false /p:KillSolidWorksOnBuild=false `
  /p:PreBuildEvent= /p:PostBuildEvent= /p:RunPostBuildEvent=Never `
  /p:OutputPath=$PrivateDebug /p:BaseIntermediateOutputPath=$PrivateObjDebug
# Repeat for Release using separate private output/intermediate directories.

& $MSBuild Tests\BendToolProfiles\ToolProfiles.Tests.csproj /t:Rebuild `
  /p:Platform=x64 /p:Optimize=true /p:DefineConstants=TRACE `
  /p:OutputPath=$PrivateTests /p:BaseIntermediateOutputPath=$PrivateObjTests
& $Tests
& $Tests --process-tests
& $Tests --collision-oracle $CollisionOut
& $Tests --envelope-oracle $AxialOut
& $Tests --native-family-oracle $NativeOut
& $Tests --process-oracle $ProcessOut
& $Python Tests\BendToolProfiles\compare_collision_with_geos.py $CollisionOut
& $Python Tests\BendToolProfiles\compare_axial_solids.py $AxialOut
& $Python Tests\BendToolProfiles\compare_native_families.py $NativeOut
& $Python Tests\BendToolProfiles\compare_process_motion.py "$ProcessOut\process-oracle.json"
& $Tests --process-company $PrivatePlacementConfig $CompanyDimDirectory $PrivateOutput
```

Existing company/holder/oracle commands are documented in the test README.
The native harness takes a private product DLL/output, original source to copy,
DIM directory, Min workbook, private placement config and
`--native-family-reference`; uniform regression adds `--require-uniform-sections`.
Its independent Python executable/script and OCP path are explicitly configured
by the existing `ADDIN_NATIVE_REFERENCE_*` environment variables. No production
dependency is installed by the add-in. The private Assembly harness/fixtures
remain local; its aggregate results are reported above honestly.

## Files and protection

Production: `ADDIN.csproj` includes, and five new files:

- `Commands/BendProcess/ProcessMotionGeometry.cs`
- `Commands/BendProcess/BendProcessModels.cs`
- `Commands/BendProcess/PlanarFormingContact.cs`
- `Commands/BendProcess/ProcessCollisionEngine.cs`
- `Commands/BendProcess/ProcessBatchWorker.cs`

Tests: `ProcessTests.cs`, `ProcessAdditionalTests.cs`,
`ProcessCompanyIntegration.cs`, `compare_process_motion.py`,
`ToolProfileTests.cs`, `ToolProfiles.Tests.csproj`,
`NativeToolLibraryValidation.cs`, README. Docs: the process contract and this report.

Check Min, Phase 3C kernel, Phase 3D worker/native capture/compiler, Planning/order,
WinForms/Designer/event handlers, other commands, DXF/Excel readers and
`tools/BendToolAudit/` have zero source diff. No original Part is changed.
Only source/test/docs are committed: no company CAD/Excel/STEP, private design
coordinates, captured source hashes, approvals, keys, dependencies or screenshots.

Unsupported real deformation/contact or missing machine/setup data remains
UNKNOWN. Phase 4B still needs reviewed full process constraints, sequence search
and whole-Part feasibility before one tool could be selected. This phase makes
no manufacturing PASS/NG, machine operation or whole-Part feasibility claim.
