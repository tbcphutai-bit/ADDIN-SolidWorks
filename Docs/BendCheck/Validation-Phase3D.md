# Check chấn Phase 3D — Windows validation, 2026-10-17

Issue [#14](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/14).
Baseline: Phase 3C / PR #13, `0f57fd2de331baebaaba1d28ab9bfeb7dc1e4ac1`.
Branch: `feature/check-chan-phase3d-sheet-envelope`.
Draft PR target: `feature/check-chan-phase3c-collision`.
No older PR or main is edited or merged. See the
[coverage, placement and worker contract](Phase3D-EnvelopeContract.md).

## Observed results

| Check | Result |
| --- | --- |
| Product Debug/x64 | PASS, 0 errors, 16 existing warnings |
| Product Release/x64 | PASS, 0 errors, same 16 existing warnings |
| Pure C# geometry/library/holder/placement/collision/envelope, DEBUG | 215 PASS / 0 FAIL; 7,964 assertions |
| Same suite, optimized TRACE-only Release/x64 | 215 PASS / 0 FAIL; 7,964 assertions |
| Existing Bend Planning regression | 24 PASS / 0 FAIL; 609 assertions |
| Phase 3C independent GEOS oracle | 552 PASS: 346 CLEAR / 201 COLLISION / 5 UNKNOWN |
| New independent analytic 3D source-volume oracle | 426 PASS: 229 CLEAR / 196 COLLISION / 1 UNKNOWN |
| Actual 18 DIM + two aggregate DXFs | 38 assertions PASS; 18 read / 17 valid closed punches / KIK open |
| Actual Master/workbook placement regression | 9 assertions PASS |
| Actual holder regression | 285 assertions PASS; 18 slots, 14 selected references, 120/120 SPLINE instances parsed |
| Actual 18-tool Phase 3C collision integration | 26 assertions PASS; 54/108 rows all UNKNOWN |
| Independent ezdxf analytic-curve comparison | 6,975 curves PASS; maximum observed error 4.55e-13 mm |
| Independent selected-holder SPLINE/INSERT comparison | 120 instances / 1,272 chords / 21,624 samples PASS; source unchanged |
| Native six-bend Part, final Debug | 903 assertions PASS; 108 Phase 3D rows UNKNOWN |
| Native six-bend Part, final Release | 903 assertions PASS; 108 Phase 3D rows UNKNOWN |
| Native uniform three-bend Part, final Release | 436 assertions PASS; 54 Phase 3D rows UNKNOWN |
| Native Assembly, final Release | Functional PASS; 1,555 assertions attempted, one inherited layout stress warning (qualified below) |

The 188 Phase 3C pure test groups are retained and 27 new groups exercise real
axial volume calculations, native-style topology and worker behavior. Results
are observed local executions, not a GitHub CI PASS or exhaustive geometry proof.
QA dependencies are outside production: Shapely 2.2.0 / GEOS 3.14.1 and ezdxf 1.4.5.
The new axial solid-reference script uses only the Python standard library.

### Failures found and limitations retained

- An early worker test incorrectly equated parallel completion order with fair
  dispatch order. Dispatch is now recorded separately and tested across bends;
  monotone progress and stable public output order are tested independently.
- The independent source-volume oracle caught an overstated clearance near an
  axial cap: a same-Z XY distance omitted nearer material outside the tool's Z
  extent. The production solver now encloses cap neighborhoods and includes the
  excluded axial-distance lower bound. A focused regression and all 426 source
  volume cases pass after this correction.
- Initial new native B-rep reads rejected loops because curve-oriented endpoint
  sense was mistaken for edge orientation. The additional capture now handles
  `CurveParamData.Sense` before coedge direction, with a unit regression. The
  existing Min reader and CAD were not changed. The six-bend native Part now
  gets past shell loop verification and remains UNKNOWN for nonprismatic trim.
- Final review found that the exact-slice witness path needed the same pair-time
  guard as the enclosing-volume path. It now checks remaining time before and
  after every kernel query, including observed-only slices; a forced-delay
  regression confirms that exhausted pair budgets cannot publish a collision.
- One Phase 3D native Debug attempt **failed** at inherited `Bad manifest does
  not block Check Min` after **739 assertions**. Its retained before/after Min
  structures compare equal, but it did not capture the exact assertion-time
  serialization. A root cause is **not established**. The harness now retains
  both exact strings at that assertion; subsequent Debug and Release runs pass
  the exact equality and complete 903 assertions. This failure is disclosed,
  not reclassified as PASS or claimed fixed in Min.
- Phase 3C's previous 731-assertion failure at the same manifest check and Phase
  3B.1's Assembly `Part initial pass` failure after 1,522 assertions remain
  historical reproducibility limitations. Their root causes are not established.
  Current Assembly tests retain the stronger Current/Done precondition and
  initial-Part diagnostic before changing the private sketch.
- The existing 1,000-pixel Assembly mock-row layout test still measures a long
  material label at **57 px versus 53 px row height**. It is recorded separately
  while functional tests continue. Thus 1,555 attempted assertions are **not**
  reported as 1,555 clean passes. No UI/Designer/handler repair is claimed.

## Geometry coverage exercised

The new tests demonstrate both volume collision and certified separation:

- Constant and stepped prismatic solids, through holes, slots/reliefs, solid
  islands in holes, local pockets, notches/trimmed edges and staggered material.
- A tab existing only between two clear observations. Observation-only data
  remains UNKNOWN; the full source-event partition proves the actual collision.
  This is the required counterexample to sparse section sampling.
- Linear translation sweeps with analytic LINE/ARC and conservative full-interval
  motion bounds; a box minus an off-axis through cylinder using analytic square
  root bounds. This cross-axis bore support is a controlled volume definition,
  not a claim to recognize that native cylindrical Part automatically.
- Exact native-style closed shells generated as analytic polycell unions: shared
  interior faces are removed, face loops/holes and opposite directed coedges are
  present. Through-hole, intermediate tab, local pocket and 24 independently
  checked cells are exercised, including translated/rotated/mirrored frames.
  The cells define exact synthetic solids, not sampled production tessellation.
- Corrupt orientation, missing coedges/counts, B-surfaces, source/frame mismatch,
  unsupported multibody scope and insufficient topology fail closed. Tool segments
  placed at different Z extents yield different results for the same XY shape.
  Cap-only/uncertainty-band contact cannot yield a material-interior witness.
- A controlled all-18 reviewed-contract fixture evaluates three bends, **54 rows**:
  **36 static COLLISION / 18 static CLEAR**. Another fixture lacks holder axial
  evidence and preserves proven Punch COLLISION with independent Holder UNKNOWN.
  All synthetic review/geometry data remains simulation-only; no winner or
  manufacturing PASS is generated.
- Worker tests cover 54/108 rows, sync/async agreement, bounded concurrency,
  tool-round-robin dispatch, atomic publication, cancel/time/query/policy failure,
  preparation errors, supersession and faulty progress listeners. During work,
  invalidating model/config lease, library/source, mapping, holder geometry,
  review expiry or axial pose review makes held results UNKNOWN. Completed job
  clocks stop without disabling live source freshness.

The 426-case independent oracle evaluates the **original source volume** as a
union of boxes or a box minus a cylinder. It does not import C# section output
as its reference. It checks every CLEAR against actual source overlap and 3D
distance, and every COLLISION witness against true occupied volume and tool Z.
No false CLEAR or false COLLISION was observed. The 552-case Phase 3C oracle
continues to check independent component-minus-hole material unions and analytic
curve bands. These references are QA; they do not grant production certificates.

## Native capture and regression

Tests used API-created, owned SOLIDWORKS 2024 x64 processes, private Part/Assembly
copies and private DLL output. No Computer Use, installed add-in replacement,
registration, user-process termination or original CAD modification was used.
Capture itself only reads geometry; deliberate sketch/rebuild/configuration tests
operate on the test copies. Original file and library source hashes stay unchanged.

| Native fixture | Uniform2DProven | AxialSlicesWithCertifiedCoverage | UnknownCoverage | Rows |
| --- | ---: | ---: | ---: | ---: |
| Existing unsupported six-bend Part | 0 | **0** | 6 | 108 |
| Existing uniform three-bend Part | 3 | 0 | 0 | 54 |

The six-bend Part coverage improves from **0/6 to 0/6**, not to a verified 3D
body. All six frames report `UNSUPPORTED_NONPRISMATIC_TRIM`; the current native
prismatic compiler cannot cover the actual trim variation. The new implementation
does execute continuous axial proof for controlled holes/trim/tab/pockets and
analytic variation, but general trimmed native B-rep intersection is still a
**code/backend limitation**. No sampled substitute or fabricated native geometry
is used to hide it. `VerifiedNative3D` is never produced.

The three-bend Part retains the existing analytic uniform proof, with one
certified interval per bend. Actual tool length/placement evidence is still
missing, so even its proved sheet coverage does not produce tool CLEAR.

For both fixtures, Min `GeometryReadCount` remains **1**. The new optional full
B-rep append captures **one body once**; reuse of the snapshot and source/evidence
refresh add **zero** COM topology reads. Native `WrongThreadCount` stays **0**;
worker preparation executes on a different managed thread without native calls.
Rebuild/configuration invalidates held coverage and results. The final six-bend
Debug/Release initial Min/dimension/Planning snapshots compare exactly with the
saved Phase 3B baseline; exact before/after bad-manifest strings also compare equal.

The final Assembly run retains selected/all scope, moved/rotated duplicate
instances, alternate referenced configuration, quantity grouping, isolated Part
captures, details, numbering/highlighting and private sketch/rebuild auto-update.
Its three file/configuration records have **5 / 3 / 3** bends and retain
**90 / 54 / 54** Phase 3D rows, all UNKNOWN. Each record has one Min geometry read;
the managed worker adds no COM reads. Layout and earlier intermittent failures
remain qualified above.

## Performance observations

Single observed local runs; these are not guaranteed latency or an existing UI
speedup. The worker API is tested; existing UI handlers still use the old path.

| Work | Observed elapsed |
| --- | ---: |
| Optimized Release, 18 controlled pairs × 3 bends, 180 2D queries, 2 workers | **57.10 ms** |
| Same async call, time until task returned to caller | **0.52 ms** |
| Single controlled approved Punch, all 54/108 slots retained | **1.01 / 2.82 ms**, 3 / 6 kernel queries |
| Native six-bend final Release, additional whole-body STA append | **614.22 ms**, once |
| Native six-bend final Release, managed 108-row batch | **34.01 ms** |
| Native uniform Part final Release, additional whole-body STA append | **328.05 ms**, once |
| Native uniform Part final Release, managed 54-row batch | **41.73 ms** |

Actual batches have no approved tool occupancy and skip numeric tool queries;
their times are **not** a benchmark for 18 physically verified company pairs.
The extra topology append has a measured STA cost. It is reused across candidates
and library changes, not performed 18 times. Complete native intersection,
geometry-specific cache optimization and UI adoption remain future work.

## Status of all 18 company tools

Available user-confirmed Master/workbook sources are the saved **unsuffixed**
files. No byte/revision identity with intended `(2)` / `(3)` files is claimed.
Common to every row: **zero verified production XY poses, zero approved Punch
axial length/position records, zero approved Holder axial length/position records,
zero verified physical pairs** and no approved exact Excel family applicability.
Selected reference heads are not automatically accepted material or mount data.

| Tool ID | Punch | Holder association | Punch / Holder / Combined |
| --- | --- | --- | --- |
| AMD-STD-003 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-004 | Closed valid | Unresolved mapping | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-0048 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-0049 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-008 | Closed valid | Unresolved mapping | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-045 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-047 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-103 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-109 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-117 | Closed valid | Unresolved mapping | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-200 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-202 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-210 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-452 | Closed valid | Unresolved mapping | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-STD-453 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-202662 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| AMD-204662 | Closed valid | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |
| KIK-7181328001 | Source-open, unchanged | Selected reference, unreviewed | UNKNOWN / UNKNOWN / UNKNOWN |

Missing company data/review is separate from the native proof backend limitation.
Completing mappings, material roles, tip/mount/relative frames, actual finite
segments and measured placements requires external operator/reviewer evidence.
The engine never creates it from illustration coordinates or Excel 4.2/6 m labels.

## Changed files and protected scope

New production files under `Commands/BendEnvelope/`:

- `SheetEnvelopeModels.cs`
- `AnalyticAxialSolids.cs`
- `NativeEnvelopeTopology.cs`
- `SheetNativeTopologyCapture.cs`
- `PrismaticEnvelopeCompiler.cs`
- `ToolAxialSetupEvidence.cs`
- `AxialCollisionEngine.cs`
- `CollisionBatchWorker.cs`

Existing production edits:

- `ADDIN.csproj`: explicit Compile Includes for the eight files.
- `Commands/BendPlacement/BendSheetSection.cs`: optional topology DTO on the body
  and internal visibility for the existing projection helper; uniform proof unchanged.
- `Commands/BendPlacement/BendPlacementNativeCapture.cs`: once-per-context STA
  topology append and post-capture freshness check.
- `Commands/BendPlacement/BendPlacementBuilder.cs` and `BendPlacementModels.cs`:
  optional deferred collision preparation, retaining synchronous default behavior.
- `Commands/BendCollision/PlacementCollisionEvaluator.cs`: expose the existing
  placement-error helper internally; no kernel algorithm change.

Tests/documentation:

- `Tests/BendToolProfiles/EnvelopeTests.cs`
- `Tests/BendToolProfiles/NativeEnvelopeTests.cs`
- `Tests/BendToolProfiles/EnvelopeWorkerTests.cs`
- `Tests/BendToolProfiles/EnvelopeOracleIntegration.cs`
- `Tests/BendToolProfiles/compare_axial_solids.py`
- `Tests/BendToolProfiles/ToolProfileTests.cs`
- `Tests/BendToolProfiles/ToolProfiles.Tests.csproj`
- `Tests/BendToolProfiles/NativeToolLibraryValidation.cs`
- `Tests/BendToolProfiles/README.md`
- This report and `Docs/BendCheck/Phase3D-EnvelopeContract.md`.

Check Min, Planning/order logic, WinForms/Designer/event handlers, existing session,
DXF/Excel readers and other commands have zero source diff. Product builds use
private output/intermediates with `KillSolidWorksOnBuild=false`, empty pre/post
events and `RunPostBuildEvent=Never`. The 16 warnings are eight unrelated baseline
CS0219/CS0649 sites repeated by WPF compilation. No CAD/Excel/Part, company source
coordinates, manifests, native snapshots, keys, approvals or screenshots are
included. Native fixtures/reports and the extended Assembly harness remain private.

Before whole-Part tool ranking, further phases must prove the remaining native
geometry, tool occupancy and before/during/after forming/contact/motion states.
Static CLEAR here does not mean the entire Part can be manufactured.
