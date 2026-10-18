# Phase 3D.1 — native coverage validation, 2026-10-18

Issue [#16](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/16).
Pinned baseline: PR #15, `283a7a77a3280dda6cc36b82412608cf518cb58b`.
Branch: `feature/check-chan-phase3d1-native-coverage`.
Draft target: `feature/check-chan-phase3d-sheet-envelope`.
No old PR or main is changed/merged. See the
[algorithm and source contract](Phase3D1-NativeCoverage.md).

## Native root cause, measured before code changes

The exact captured body has **56 faces / 112 edges**: 42 planes, 14 cylinders,
84 LINE and 28 circular edges; four circular edges are periodic, four face loops
are inner loops, and no loops are singular. There are no B-surfaces or unknown
curve families in this body. The initial fail is a deliberately restrictive
Phase 3D rule, not a broken native shell: every edge must be at a single axial
station or parallel to the inspected bend axis. Transverse bend rims violate it.

All six frames have 56 station LINE edges and 28 axial LINE edges. The other
classification counts are:

| Frames | Station circular edges | Nonprismatic circular edges | Planar caps / axial walls | Axial / transverse cylinders |
| --- | ---: | ---: | ---: | ---: |
| 805, 811, 867, 873 | 20 (including 4 full circles) | 8 | 20 / 22 | 8 / 6 |
| 890, 906 | 12 (including 4 full circles) | 16 | 14 / 28 | 4 / 10 |

The first group's rejecting edges are **80–83 and 108–111**, incident to faces
38–41 and 52–55. The second group's rejecting edges are **36, 38, 40, 42, 46, 48,
50, 52, 56, 58, 60, 62, 66, 68, 70, 72**. These are analytic circular trims of
transverse bends. Edge and face IDs are local capture indices; no part name,
coordinate, dimension or private source hash is published here.

The old rule also cannot reach correct full-circle plane/sense processing.
The new append obtains that evidence directly from native CircleParams and
CurveParamData. The new compiler validates every plane/cylinder patch, partitions
at analytic axial extrema, constructs exact material/void sections and encloses
the complete changing boundary over each interval. It does not omit these faces,
join gaps, tessellate a silhouette or certify a few sampled sections.

## Coverage before and after — actual six-bend Part

Before/after captured **face and edge arrays compare exactly equal** privately.
The changed hash additionally binds the new circle plane/sense records; no source
geometry was altered to obtain a better result.

| Feature | Phase 3D tier / reason | Phase 3D.1 tier | Intervals | Independent material / void checks | Actual 18 pairs |
| --- | --- | --- | ---: | ---: | --- |
| 805 | UnknownCoverage / UNSUPPORTED_NONPRISMATIC_TRIM | AxialSlicesWithCertifiedCoverage | 11 | 528 / 528 | UNKNOWN |
| 811 | UnknownCoverage / UNSUPPORTED_NONPRISMATIC_TRIM | AxialSlicesWithCertifiedCoverage | 11 | 528 / 528 | UNKNOWN |
| 867 | UnknownCoverage / UNSUPPORTED_NONPRISMATIC_TRIM | AxialSlicesWithCertifiedCoverage | 11 | 528 / 528 | UNKNOWN |
| 873 | UnknownCoverage / UNSUPPORTED_NONPRISMATIC_TRIM | AxialSlicesWithCertifiedCoverage | 11 | 528 / 528 | UNKNOWN |
| 890 | UnknownCoverage / UNSUPPORTED_NONPRISMATIC_TRIM | AxialSlicesWithCertifiedCoverage | 11 | 252 / 252 | UNKNOWN |
| 906 | UnknownCoverage / UNSUPPORTED_NONPRISMATIC_TRIM | AxialSlicesWithCertifiedCoverage | 11 | 252 / 252 | UNKNOWN |

**Native sheet coverage increases from 0/6 to 6/6.** The method is
`CompleteOrthogonalPlaneCylinderSectionFamilyV1`, over the entire captured owner
body in each frame. No bend in this particular Part remains UnknownCoverage.
There are still **108 UNKNOWN tool rows**, with independent Punch/Holder statuses:
sheet geometry proof is not production tool/mount/occupancy approval.

The original three-bend uniform Part stays **3/3 Uniform2DProven**, one interval
per bend. Its 54 real tool rows remain UNKNOWN. No VerifiedNative3D tier, sequence,
winner or whole-Part manufacturing PASS is introduced.

## Independent native B-rep evidence

An API-owned SOLIDWORKS process opens a private copy. The test exports a private
STEP copy with Copy/Silent/AvoidRebuild flags; model path/stamp and original file
hash checks ensure that the source Part is not changed. OpenCascade 7.8.1.1 then
reads the STEP independently, checks the B-rep's validity and single-solid scope,
and classifies points against that solid. Exact SOLIDWORKS extrema guard STEP
units/frame: the observed maximum bound difference is **1.78e-15 mm**.

For each completed Debug and Release six-bend run:

- **5,232 / 5,232 points checked**, zero skipped: 2,616 inside and 2,616 outside.
  Samples cover every source partition at multiple interior axial positions,
  including both sides of source boundaries. Every slice classification matches
  the independent B-rep; no true inside sample falls outside its interval envelope.
- **12 actual collision witnesses** from controlled finite QA probes are inside
  the independently parsed native material. Each bend exercises two COLLISION
  probes and one whole-source CLEAR probe beyond an independently checked native
  extreme. These are software probes, not company tools or mounting approvals.
- **0 false material classifications / 0 missed material classifications** in
  this observed dataset. The same comparison on the uniform Part checks **288 /
  288 points**, plus six collision witnesses and three separated finite probes.

These point comparisons are independent **QA**, not the continuous coverage
proof. Production coverage comes from the complete analytic shell and interval
boundary bounds. STEP, source coordinates, raw native snapshots and exports stay
private; only counts and reasons are published. OCP/VTK are QA-only dependencies.

## Build and regression matrix

| Check | Observed result |
| --- | --- |
| Product Debug/x64 | PASS; 0 errors, 16 baseline warnings |
| Product Release/x64 | PASS; 0 errors, same 16 baseline warnings |
| Managed suite DEBUG | **222 PASS / 0 FAIL; 8,130 assertions** |
| Managed suite optimized TRACE-only Release | **222 PASS / 0 FAIL; 8,130 assertions** |
| Existing Bend Planning | **24 PASS / 0 FAIL; 609 assertions** |
| Phase 3C GEOS oracle | **552 PASS**: 346 CLEAR / 201 COLLISION / 5 UNKNOWN |
| Phase 3D original-volume oracle | **426 PASS**: 229 CLEAR / 196 COLLISION / 1 UNKNOWN |
| New native-style B-rep family oracle | **480 PASS**: 182 CLEAR / 297 COLLISION / 1 UNKNOWN |
| Actual 18 DIM + aggregates | **38 assertions PASS**; 18 read, 17 valid closed, KIK open |
| Actual Master/workbook / holder / collision regressions | **9 / 285 / 26 assertions PASS** |
| Independent DXF and holder SPLINE QA | **6,975 curves; 120 instances / 1,272 chords / 21,624 samples PASS** |
| Final native six-bend Part Debug | **947 assertions PASS**, 6/6 envelope coverage, 108 real-tool UNKNOWN rows |
| Final native six-bend Part Release | **947 assertions PASS**, same coverage and rows |
| Final native uniform three-bend Part Release | **465 assertions PASS**, unchanged 3/3 uniform coverage |
| Final native Assembly Release | **Functional PASS; 1,555 assertions attempted**, one inherited layout stress warning |

All **215 Phase 3D groups / 7,964 baseline assertions are retained**. Seven new
groups cover native full-circle holes, transverse quarter bends, complete interval
enclosures, transforms/reflected frames, reversed circle parameterization,
missing/inconsistent source/sense/B-surfaces/multibody, nonlinear error near a
cylindrical tangent, freshness and the snapshot comparison regression.

The new 480-case oracle parses **native-style analytic shells**, not supplied
`AnalyticSheetEnvelope` volumes. An independent Python reference computes the
original box-minus-cylinder and quarter-annular extrusion volumes. It verifies
true overlap, 3D clearance lower bounds and source-interior witnesses. No false
CLEAR/COLLISION was observed in any of the three independent oracle corpora.
These are test observations, not exhaustive support for arbitrary native CAD.

Native Assembly retains selected/all scope, moved/rotated duplicates, alternate
referenced configurations, quantity grouping, isolated Part capture, highlighting,
details and sketch/rebuild auto-update. Its 5 / 3 / 3-bend file/configuration
records retain **90 / 54 / 54** Phase 3D worker rows, all UNKNOWN for real tools.
The extra native capture stays once per snapshot; source-only refresh adds no
COM traversal, Check Min GeometryReadCount stays **1**, and worker COM count stays
**0**. Rebuild/config changes invalidate old results. Original CAD hashes hold.

## Failed attempts and inherited test issues

1. The first newly added unit-test build failed because the test used a nonexistent
   three-argument `CollisionWork` constructor. The harness was corrected to use
   the existing query/token API; production kernel code was not changed.
2. The first six-bend native run again stopped at **739 assertions**, at
   `Bad manifest does not block Check Min`. Its exact before/after strings were
   retained. Parsed structures, all values and candidate array order match; only
   JSON **object property ordering** differs. This reproduced failure is a test
   string-equality false positive, not changed Min/Planning data. Test-only
   canonicalization sorts object keys while preserving every field/value and
   array order. A unit test still rejects changed candidate sequence or numbers.
   All completed final native runs pass the canonical comparison. No Check Min
   or Planning production repair was made.
3. Two attempted SOLIDWORKS ray-reference runs failed at the reference API with
   `RPC_E_SERVERFAULT`, after 167 assertions: first the obsolete ModelDoc2 API,
   then the current high-precision extension with normalized, bounded batches.
   Those attempts are **not** counted as independent reference PASS. The final
   reference uses the separately parsed STEP/OpenCascade solid instead; the
   failing ray code is not shipped. Failures occurred in owned test processes;
   no original CAD edit or production worker COM path was introduced.
4. Historical Phase 3C 731 / Phase 3D 739 manifest failures did not retain exact
   strings, so their causes cannot be conclusively reassigned. Object ordering
   is a supported hypothesis consistent with those equal saved structures.
5. The older Assembly initial-pass instability did not reproduce in this final
   run. The harness retains Current/Done gating and the initial-Part diagnostic
   before a private sketch edit; it recorded a current, completed initial pass
   followed by the expected updated result. The historical cause is unproven.
6. The inherited 1,000-pixel layout stress warning remains **57 px text / 53 px
   row height**. Therefore 1,555 attempted assertions are not described as 1,555
   clean passes or a clean UI layout result. UI/Designer/handlers are unchanged.

## Performance observations

Single local observations; no CI or guaranteed latency is claimed.

| Work | Observed elapsed |
| --- | ---: |
| Six-bend Debug additional whole-body STA append | 649.65 ms, once |
| Six-bend Debug managed coverage + 108-row batch | 65.89 ms |
| Six-bend Release additional whole-body STA append | 647.60 ms, once |
| Six-bend Release managed coverage + 108-row batch | 85.64 ms |
| Uniform Part Release additional STA append / managed 54-row batch | 333.58 / 42.17 ms |
| Controlled all-18 × 3 bends Release, 180 2D calls, 2 workers | 58.04 ms; caller return 0.69 ms |

Real-tool batches have no approved physical occupancy and skip numeric tool
checks; their elapsed times are not a benchmark of 18 fully verified company
setups. Extra proof work replaces the previous immediate rejection. Native QA
STEP export and independent reference run separately from production preparation.
Existing UI flow still uses its compatibility path; no UI speedup is asserted.

## Reproduction commands

Keep all outputs/intermediates outside the checkout. Product builds were run with:

```powershell
& $MSBuild ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 `
  /p:UseSharedCompilation=false /p:KillSolidWorksOnBuild=false `
  /p:PreBuildEvent= /p:PostBuildEvent= /p:RunPostBuildEvent=Never `
  /p:OutputPath=$PrivateDebugOutput /p:BaseIntermediateOutputPath=$PrivateDebugObj
# Repeat with Release and separate private output/obj paths.
& $MSBuild Tests/BendToolProfiles/ToolProfiles.Tests.csproj /t:Rebuild `
  /p:UseSharedCompilation=false /p:Configuration=Release /p:Platform=x64 `
  /p:Optimize=true /p:DefineConstants=TRACE `
  /p:OutputPath=$PrivateTestOutput /p:BaseIntermediateOutputPath=$PrivateTestObj
& $ToolProfilesTestExe
& $ToolProfilesTestExe --collision-oracle $PrivateCollisionOracle
python Tests/BendToolProfiles/compare_collision_with_geos.py $PrivateCollisionOracle
& $ToolProfilesTestExe --envelope-oracle $PrivateAxialOracle
python Tests/BendToolProfiles/compare_axial_solids.py $PrivateAxialOracle
& $ToolProfilesTestExe --native-family-oracle $PrivateNativeOracle
python Tests/BendToolProfiles/compare_native_families.py $PrivateNativeOracle
```

Native reference setup used an isolated CPython 3.11 x64 target with
`cadquery-ocp==7.8.1.1` and its `vtk==9.3.1` DLL dependency (QA only):

```powershell
$env:PYTHONPATH = $PrivateOcpTarget
$env:ADDIN_NATIVE_REFERENCE_PYTHON = $PythonExe
$env:ADDIN_NATIVE_REFERENCE_SCRIPT = (Resolve-Path Tests/BendToolProfiles/compare_native_step.py).Path
& $NativeToolLibraryTestExe $ProductDll $PrivateNativeOutput $PrivateSixBendPart `
  $CompanyDimDirectory $MinExcelPath $PrivatePlacementConfig `
  --capture-private --native-family-reference
# For the three-bend fixture add --require-uniform-sections.
```

The native harness invokes the configured Python/script, awaits it and requires
a passing sanitized reference summary. It retains the exact failure output
privately. Scripts and sample comparisons do not replace production proof.
Original library/Planning/Assembly regression commands remain those documented
in their test READMEs; the extended Assembly fixture/harness stays private.

## Remaining production blockers and changed files

All 18 company tools remain `CHUA_XAC_MINH`: zero approved finite Punch/Holder
length/position records, physical pair or production work poses; 14 holder
references lack approved material roles, four mappings (004/008/117/452) remain
unresolved, and KIK remains source-open. No tool data or source DXF/Excel is
repaired or inferred. These blockers are separate from the resolved six-bend
sheet coverage restriction.

Geometry outside the admitted orthogonal plane/cylinder patch family can still
be UnknownCoverage with an entity/class reason: B-surfaces, oblique or irregular
periodic trims, noncircular curves, unresolved dimensions or multibody scope.
Whole-Part forming sequence, contact, motion and winner selection remain future
work; static CLEAR is not manufacturing PASS.

Production edits are limited to five files:

- `Commands/BendEnvelope/OrthogonalNativeEnvelopeCompiler.cs` (new algorithm).
- `Commands/BendEnvelope/NativeEnvelopeTopology.cs` (circle evidence/hash).
- `Commands/BendEnvelope/SheetNativeTopologyCapture.cs` (same STA append).
- `Commands/BendEnvelope/PrismaticEnvelopeCompiler.cs` (specific fallback and diagnostics).
- `ADDIN.csproj` (explicit Compile Include).

Test/documentation changes:

- `Tests/BendToolProfiles/NativeCylinderTests.cs`
- `Tests/BendToolProfiles/NativeFamilyOracle.cs`
- `Tests/BendToolProfiles/NativeFamilyReference.cs`
- `Tests/BendToolProfiles/NativeSnapshotJson.cs`
- `Tests/BendToolProfiles/compare_native_families.py`
- `Tests/BendToolProfiles/compare_native_step.py`
- `Tests/BendToolProfiles/NativeToolLibraryValidation.cs`
- `Tests/BendToolProfiles/NativeToolLibrary.Validation.csproj`
- `Tests/BendToolProfiles/EnvelopeTests.cs`
- `Tests/BendToolProfiles/ToolProfileTests.cs`
- `Tests/BendToolProfiles/ToolProfiles.Tests.csproj`
- `Tests/BendToolProfiles/README.md`
- `Docs/BendCheck/Phase3D1-NativeCoverage.md`
- This validation report.

The Phase 3C collision kernel, Phase 3D worker, Check Min, Planning/order logic,
WinForms/Designer/event handlers, other commands and DXF/Excel readers have zero
source diff. Only source/test/docs are committed: no CAD/STEP/Excel, company
coordinates or filenames, source hashes, private captures/manifests, approvals,
keys, compiled dependencies or screenshots.
