# Check chấn Phase 3C — Windows validation, 2026-10-17

Issue [#12](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/12).
Baseline: PR #11, `9f2f6bdb90974c727289de4cbaef69d7c77a42e5`.
Branch: `feature/check-chan-phase3c-collision`.
Draft PR target: `feature/check-chan-phase3b1-tool-holder`.
No older PR/main was edited or merged. See the
[algorithm and evidence contract](Phase3C-CollisionContract.md).

## Observed build and test results

| Check | Result |
| --- | --- |
| Product Debug/x64 | PASS, 0 errors, 16 existing warnings |
| Product Release/x64 | PASS, 0 errors, same 16 existing warnings |
| Pure C# DXF/region/datum/placement/holder/collision, Debug | 188 PASS / 0 FAIL; 7,624 assertions |
| Same suite, optimized Release/x64 | 188 PASS / 0 FAIL; 7,624 assertions |
| Existing Bend Planning regression | 24 PASS / 0 FAIL; 609 assertions |
| Independent Shapely 2.2.0 / GEOS 3.14.1 collision oracle | 552 cases PASS: 346 CLEAR / 201 COLLISION / 5 UNKNOWN; 0 false CLEAR / 0 false COLLISION observed |
| Actual 18-tool collision integration, synthetic 3/6-bend sheet | 26 assertions PASS; 54/108 rows, all UNKNOWN with reasons |
| Actual 18 DIM + two aggregate DXFs | 38 assertions PASS; 18 read / 17 closed valid / KIK open |
| Actual Master/workbook placement regression | 9 assertions PASS |
| Actual 18-slot holder regression | 285 assertions PASS; 14 selected references, 120/120 SPLINE instances parsed |
| Independent analytic DXF QA, ezdxf 1.4.5 | 6,975 curves PASS; maximum observed error 4.55e-13 mm |
| Independent SPLINE/INSERT QA, ezdxf 1.4.5 | 120 instances / 1,272 chords / 21,624 samples PASS; source unchanged |
| Native six-bend Part, instrumented Debug repeat | 893 assertions PASS; 108 collision rows UNKNOWN |
| Native six-bend Part, Release | 893 assertions PASS; 108 collision rows UNKNOWN |
| Native uniform three-bend Part, Release | 426 assertions PASS; 54 collision rows UNKNOWN |
| Native Assembly, Release | Functional PASS; 1,540 assertions attempted; 1 existing layout stress warning |

The first Phase 3C native Debug attempt **failed** at `Bad manifest does not
block Check Min`, after 731 assertions. It did not retain that assertion's
actual snapshot, so its cause is not proven. The test now saves before/after
manifest snapshots, item identity and item error. The instrumented Debug
repeat and Release run both completed, including this exact comparison, with
unchanged item identity, no item error and equal Min/Planning snapshots.
No Min/UI repair or proven root-cause fix is claimed. This initial failure
remains a disclosed native reproducibility limitation, not a PASS.

There was also a **previous Phase 3B.1** intermittent Assembly `Part initial
pass` failure after 1,522 assertions; that earlier attempt had no initial-Part
diagnostic. Its instrumented/Current-gated repeat passed functional scenarios.
Phase 3C retains that stronger test precondition and initial-Part diagnostics;
the current run recorded `Current / Done / Default / Đạt` before the sketch
edit and then updated to NG after rebuild. This does not establish a root
cause for the historical failure.

The Assembly mock-row test still reports a long existing material label at
1,000-pixel form width: **57 px measured versus 53 px row height**. The harness
records this separately and continues its other functional assertions.
Therefore 1,540 attempted assertions are **not** reported as 1,540 clean passes,
and the existing UI is not claimed to have a clean layout test.

## Geometry exercised

The 28 new test groups extend 160 baseline groups. They execute actual
separation/intersection/containment decisions, not fabricated DTO statuses:

- Separated, overlapping, identical and entirely contained rectangles;
  circle/circle, LINE/ARC semicircle, minor/major and CW/CCW arcs; concave L/U/Z
  and deep returns; exact edge/corner/tangent contact and near-threshold gaps.
- Solid, hole, island, independent and overlapping component unions with holes
  subtracted per component. Adding a solid cannot erase demonstrated overlap.
- Conservative boundary-band separation, certainly occupied interior overlap
  and band-only potential overlap. Increasing uncertainty cannot create CLEAR.
- Rotation/mirror invariance, wrong frames, source-open punches, empty/invalid
  topology, unsupported full-sheet coverage, stale sources/model leases and
  held-result invalidation. Cancellation, elapsed time, work and subdivision
  limits explicitly return UNKNOWN. The kernel also executes on a managed
  worker with no SOLIDWORKS objects.
- The existing bounded SPLINE backend supplies all chords and deviation bands.
  The test deliberately contrasts a raw holder containing only its closing
  LINE with the full reviewed curved material; the solver uses the latter and
  produces all three statuses correctly.
- Three/six-bend fixtures keep 54/108 rows and independent punch/holder results.
  A separate **all-18 controlled synthetic external-contract fixture** supplies
  54 exact setup records without selecting a tool: each of its three bends
  has 6 punch collisions, 6 holder collisions and 6 CLEAR pairs. Thus 36 rows
  collide and 18 are clear at the observed pose, while manufacturing remains
  CHUA_XAC_MINH and no winner is selected. These synthetic triangles/rectangles,
  keys and records are software tests, not company CAD or actual inspections.
- Production work-pose contract binds the exact model/section/frame/source/
  seating record; another capture or a signed mismatched frame cannot reuse it.

The independent oracle consumes only explicitly controlled synthetic captures,
independently constructs component-minus-hole unions, and checks positive
clearance certificates and robust interior witnesses. QA-only ARC linearization
uses at most `1e-7 mm` sagitta plus endpoint/numeric allowance. Eroded/expanded
reference geometry accounts for source bands before assessing witnesses and
clearance lower bounds. Production is analytic and has no Shapely dependency.
All 552 fixtures passed; this is observed coverage, not exhaustive proof of
every possible geometry. Five cases deliberately remain UNKNOWN.

## Actual local source status — all 18 tools

The available, user-confirmed saved Master/workbook files have **no (2)/(3)
suffix**. Their actual hashes are retained privately. No byte-identity or
approved-revision claim is made about differently named revisions.

Common to **all rows**: no verified tip/mount/local frame, physical pair or
workpiece contact/approach record; no approved exact Excel family applicability.
Common to the **14 selected holders**: no approved material-role/region or
physical setup evidence. A selected reference is not verified metal fill.

| Tool ID | Punch region | Holder association | Punch / Holder / Combined |
| --- | --- | --- | --- |
| AMD-STD-003 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-004 | Valid | Unresolved | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-0048 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-0049 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-008 | Valid | Unresolved | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-045 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-047 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-103 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-109 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-117 | Valid | Unresolved | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-200 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-202 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-210 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-452 | Valid | Unresolved | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-453 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-202662 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-204662 | Valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| KIK-7181328001 | Source-open, not repaired | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |

Totals: 18 punches read, 17 valid regions, 14 fully parsed selected holder
inventories, 120 supported SPLINE instances, **0 reviewed material regions,
0 verified datum sets, 0 physical pairs, 0 production work poses**. All 18
manufacturing conclusions remain CHUA_XAC_MINH. The four unresolved mappings
are never attached to a nearby or repeated INSERT to make tests pass.

Actual-company integration tests use real tool sources and an explicitly
synthetic sheet; native tests independently use real Part copies. Both keep
all 18 slots even when mapping/geometry/evidence is missing. The six-bend
Part has unsupported holes/trim/axial variation; it cannot acquire an invented
2D envelope. The uniform three-bend Part has **16 actual full boundary curves
per section**, but actual tool setup is still unverified. Before/during/after
states remain UNKNOWN in every real-source result.

## Native regression and protected scope

Owned SOLIDWORKS 2024 API processes, STA and private copies were used. No
Computer Use, debugger attachment, user-process termination, installed DLL/PDB
replacement or registration was performed. Original Part and CAD source hashes
remain unchanged. The tests mutate only their private copies for rebuild,
configuration and dimension scenarios.

The initial six-bend Debug and Release Min/dimension/limit/Planning/UI-column
snapshots exactly equal the saved Phase 3B snapshot. GeometryReadCount is **1**;
file/evidence changes add no SOLIDWORKS arc metadata reads. Missing directory,
bad manifest, malformed datum and holder/seating/authority evidence recover
without recapturing model geometry. Held collision results invalidate after
rebuild/config changes and expose no stale usable certificate.

Assembly selection/all scope, duplicate moved/rotated instances, alternate
referenced configuration, quantity grouping, isolated capture, details,
numbering/highlighting and sketch-edit/rebuild auto-update remain functional.
The three file/config records contain **90 / 54 / 54** collision rows, all
UNKNOWN, for **5 / 3 / 3** bends. Each has one geometry read. See the native
reproducibility/layout qualifications above.

Product builds use separate private output/intermediate paths with
`KillSolidWorksOnBuild=false`, empty pre/post-build events and
`RunPostBuildEvent=Never`. The 16 warnings are the same eight unrelated baseline
CS0219/CS0649 sites repeated by WPF temporary compilation. No CI result is
claimed; all reported executions were observed locally.

The inherited pure-C# diagnostic injection test originally assumed DEBUG was
always defined. Its first TRACE-only optimized run failed that test because
the Release compiler correctly omits `Debug.WriteLine`. The harness now checks
injected isolation under DEBUG and checks omission without DEBUG, matching the
existing native harness's contract. No production diagnostic code was changed.

Protected production files have **zero diff** against baseline: Check Min,
WinForms/Designer/handlers, session/capture/reader implementations, candidate
order logic, Excel data and all other commands. The only existing production
edits are placement model/builder additions and explicit project includes.

## Changed files

- `Commands/BendCollision/CollisionModels2D.cs`
- `Commands/BendCollision/CollisionInterval.cs`
- `Commands/BendCollision/CollisionGeometry2D.cs`
- `Commands/BendCollision/CollisionPointLocation2D.cs`
- `Commands/BendCollision/CollisionEngine2D.cs`
- `Commands/BendCollision/CollisionWorkPoseEvidence.cs`
- `Commands/BendCollision/CandidateCollisionResult.cs`
- `Commands/BendCollision/PlacementCollisionEvaluator.cs`
- `Commands/BendPlacement/BendPlacementModels.cs`
- `Commands/BendPlacement/BendPlacementBuilder.cs`
- `ADDIN.csproj`
- `Tests/BendToolProfiles/CollisionTests.cs`
- `Tests/BendToolProfiles/CollisionPlacementTests.cs`
- `Tests/BendToolProfiles/CollisionCompanyIntegration.cs`
- `Tests/BendToolProfiles/compare_collision_with_geos.py`
- `Tests/BendToolProfiles/ToolProfileTests.cs`
- `Tests/BendToolProfiles/ToolProfiles.Tests.csproj`
- `Tests/BendToolProfiles/HolderTests.cs` (extend synthetic helper to 18 IDs)
- `Tests/BendToolProfiles/NativeToolLibraryValidation.cs`
- `Tests/BendToolProfiles/README.md`
- `Docs/BendCheck/Phase3C-CollisionContract.md`
- `Docs/BendCheck/Validation-Phase3C.md`

No CAD/Excel/Part, private coordinates, manifests, artifacts, keys, approvals,
native captures or screenshots are included in this branch.

## Remaining limitations and next work

| Category | Still required |
| --- | --- |
| Actual source approval | Exact holder associations for 004/008/117/452, approved intended source revision and complete approved KIK source if required |
| Actual material evidence | Classify every selected holder entity, regions/holes/islands/reference exclusions and component union, with authorized external review |
| Actual setup evidence | Verified tip/mount/local frame, relative seating/physical inspection and per-capture contact/approach approval |
| Excel applicability | Reviewed exact family/tool/machine association; no hardcoded 80/105/4200/6000 limits |
| Workpiece coverage | Multiple proven slices or native 3D for trim/holes/axial variation; a uniform single-section proof is the current supported envelope |
| Conservative solver limits | Very close bands, difficult partial-arc point distances, unresolved topology, missed robust witnesses or exhausted budgets may return UNKNOWN |
| Contact/motion | Intended forming-contact semantics, insertion/withdrawal and swept collision are not inferred from this static pose |
| Scheduling | The typed kernel is worker-safe; existing builder execution remains synchronous. UI batch scheduling for many fully approved expensive candidates is future work |
| Validation | Historical Assembly flakiness, one new un-reproduced native manifest comparison failure and the existing narrow-layout warning remain disclosed |
| Phase 4 | Validate all required states and feasible sequences, then rank one common verified tool for the entire Part |

This phase delivers real geometric computation and all-tool result plumbing.
It does not select a winner, certify an actual machine setup or equate one
clear pose with a manufacturable Part.
