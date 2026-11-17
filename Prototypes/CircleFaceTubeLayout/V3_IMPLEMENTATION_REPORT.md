# CIRCLE ALONG FACE V3 — IMPLEMENTATION & VERIFICATION REPORT

- **Repository:** `https://github.com/tbcphutai-bit/ADDIN-SolidWorks`
- **Pull Request:** `#65` (`[DRAFT] Circle Along Face V3 — opt-in Smart Symmetric Overhang`) — Draft, unmerged
- **Branch:** `feature/circle-face-v3-smart-overhang-20261117`
- **Base Branch (PR #64):** `feature/circle-face-custom-equal-both-ends-20261117` (`782a84d18d423c97da235690a5018aa8d48b7987`)
- **Reviewed Baseline Commit:** `523a760`
- **Anti Fix & Optimization Commit:** `9ad5e62b6f33d59198409674e858990d41ad0e77` (`9ad5e62`)
- **SolidWorks Test Environment:** SOLIDWORKS 2024 SP5.0 (`rev 32.5.0`), Windows x64, .NET Framework 4.8, C# 7.3

---

## 1. Executive Summary

Circle Along Face V3 (**Smart Symmetric Overhang**, opt-in and restricted to `Custom + EqualPitch`) was reviewed, debugged, optimized, compiled for `Debug|x64` and `Release|x64`, and verified end-to-end against SOLIDWORKS 2024 SP5.0 on copies of the real `4313-04-HU-H1` solid plate, `6866` twisted surface sheet, planar curved sector (`surface_type=4001`), and `4316` assembly.

Key outcomes:
1. **Fixes in commit `9ad5e62`:** Resolved 6 `CS0649` `/warnaserror` compilation errors in standalone `GeometryTests.exe`, guarded `CircleFaceTubeLayoutForm.InvalidatePreview()` against control-update reentrancy, replaced misleading `"min NaN mm · BREP: Clear"` UI text with `"n/a (geometry invalid) · BREP: Skipped (geometry invalid)"` when geometry fails before B-rep, and added explicit baseline-preserved UI text when `N` fits at the user's positive margin.
2. **Performance optimization without relaxing safety checks:** Hoisted rail curve/cache precomputation (`RailEvaluatorSet`) out of the 25-step overhang search loop and rejected geometry-invalid overhang trials immediately in 1 pass instead of looping 8 additional lifts. Worst-case `N=41` search time dropped from **38.8–39.1 s down to 7.5–9.0 s** (**4.17x–5.17x speedup**) with **bit-for-bit identical** minimum overhang thresholds `H`, clearances, and `lift` values. Also added fast termination (`SMART_OVERHANG_STOP reason=flipped_solid_material_clash`) when all tubes hit a solid body under `Flip Side`.
3. **Minimum overhang thresholds verified on real models (`OD=20 mm`, `M=5 mm`, `Hmax=12 mm`):**
   - `4313` (`flip=False`, solid): `N=30 -> H=0` (`M=5.000 mm` preserved); `N=40 -> H=2.000 mm` (no center extrapolation); `N=41 -> H=12.000 mm` (`center_ext_max=2.248 mm`).
   - `6866` (`flip=False`, twisted surface): `N=30 -> H=0` (`M=5.000 mm` preserved); `N=40 -> H=0.000 mm` (`effM=0.000 mm`, no extrapolation); `N=41 -> H=9.000 mm` (no extrapolation).
   - `6866_flip` (`flip=True`, twisted surface): `N=30 -> H=0` (`M=5.000 mm` preserved); `N=40 -> H=1.500 mm` (no extrapolation); `N=41 -> H=11.500 mm` (`center_ext_max=1.748 mm`).
4. **Independent 3D SOLIDWORKS B-rep confirmation:** Constructing all 41 full-length `φ20` cylinder bodies via `Modeler.CreateBodyFromCyl` and intersecting (`SWBODYINTERSECT`) every plausible pair and the owner body yielded **0 cylinder-cylinder and 0 cylinder-owner intersections** on both `4313 (N=41, H=12)` and `6866_flip (N=41, H=11.5)`.
5. **Assembly multi-component finding:** In `copy_4316.SLDASM` (57 components), inspecting all *other* assembly components independently shows `axis=30` (even on `N=30, M=5 mm`), `axis=40` (`N=40, H=2`), and `axis=40..41` (`N=41, H=12`) intersect adjacent solid component `4316-04-KDA1-o1-1`. This confirms V3's documented scope limit: the add-in checks tube-to-tube and tube-to-selected-owner-body collisions only, not surrounding assembly parts.

---

## 2. Modified Files in PR #65 Scope

`git diff --name-only origin/feature/circle-face-custom-equal-both-ends-20261117..9ad5e62b6f33d59198409674e858990d41ad0e77` contains only:
- `Commands/CircleFaceTubeLayout/CircleFaceTubeLayoutCommand.cs` (from PR #65 base)
- `Commands/CircleFaceTubeLayout/CircleFaceTubeLayoutForm.cs` (PR #65 base + `9ad5e62`)
- `Commands/CircleFaceTubeLayout/SwFaceLayout.cs` (PR #65 base + `9ad5e62`)
- `Commands/CircleFaceTubeLayout/TubeLayoutSolver.cs` (PR #65 base + `9ad5e62`)
- `Prototypes/CircleFaceTubeLayout/V3_SMART_OVERHANG_IMPLEMENTATION.md` (PR #65 base)
- `Tests/CircleFaceTubeLayout/GeometryTests.cs` (PR #65 base + `9ad5e62`)

No project files (`.csproj`), COM registration scripts, or other commands were modified.

---

## 3. Code Audit & Changes Made in `9ad5e62`

### 3.1 `Commands/CircleFaceTubeLayout/TubeLayoutSolver.cs`
- Initialized the 6 V3 metadata fields on `TubeLayout` (`SmartOverhangEnabled = false`, `CenterExtrapolated = false`, `AppliedOverhang = 0`, `MaximumCenterExtension = 0`, `EffectiveCapMargin = 0`, `OverhangSearchAttempts = 0`) so standalone `/warnaserror` test compilation without `SwFaceLayout.cs` builds cleanly without `CS0649`.

### 3.2 `Commands/CircleFaceTubeLayout/SwFaceLayout.cs`
- Added private `RailEvaluatorSet` inside `SwFaceLayout` to create `SmallInside`, `LargeInside`, `SmallExtended`, `LargeExtended`, and precomputed `CacheRail` tables **once per `Analyze()` invocation** instead of rebuilding ~560 CAD curve/surface samples on each of up to 25 overhang search iterations.
- Extended `AnalyzeOne` with `skipSheetOnGeometryFail`: when `Analyze()` runs V3 overhang trials (`baseline` before fallback and `h = 0..maxOverhang`), any trial whose exact 3D cylinder geometry fails (`layout.Issue != null`) rejects immediately on pass 1 instead of running 8 futile outward lift retries or SolidWorks B-rep booleans. Every geometry-valid candidate (`layout.Issue == null`) still executes all GJK separation checks (`VerifyExact`) and SolidWorks `Operations2(SWBODYINTERSECT)` sheet checks (`CheckSheet`).
- Skipped redundant duplicate `h = 0` check when `margin == 0` and `baseline` already failed tube-tube separation.
- Added early stop (`SMART_OVERHANG_STOP reason=flipped_solid_material_clash`) both after `baseline` (when pair-clear at `M`) and inside the `h` loop whenever `flip && ownerBody.GetType() == swSolidBody && candidate.InterferingPairCount == 0 && candidate.SheetClashCount == count.Value`, preventing repeated ~33 s multi-pass B-rep runs on later `h` steps that keep interior tubes inside the solid.

### 3.3 `Commands/CircleFaceTubeLayout/CircleFaceTubeLayoutForm.cs`
- Added `updatingControls` guard inside `InvalidatePreview()` to prevent recursive reentrancy when `maxOverhang.Maximum = diameter.Value` clamps `maxOverhang.Value`.
- Fixed Preview status formatting when `double.IsNaN(result.MinimumSheetEstimate)` (skipped B-rep on geometry-invalid layout): prints `BREP: Skipped (geometry invalid)` and `Sheet clearance: n/a (geometry invalid)` rather than `min NaN mm · BREP: Clear`.
- Added explicit baseline message when `OverhangSearchAttempts == 0 && result.Issue == null`: `"Smart Overhang: baseline margin {0:F3} mm preserved (cap protrusion 0.000 mm; centers on selected rails)"`.

### 3.4 `Tests/CircleFaceTubeLayout/GeometryTests.cs`
- Added a curved + twisted (`R=3247 / R=4323.16`, `twist=0.07 rad`) tangent-extrapolated `VerifyExact` + `Flip` verification for `N=41, H=12 mm` inside `TestV3OverhangGeometry()`.

---

## 4. Build & Pure Geometry Test Results (Post-`9ad5e62`)

| Check | Command | Exit Code | Notes |
|---|---|---:|---|
| Standalone `GeometryTests.exe` build (`/warnaserror`) | `csc.exe /nologo /langversion:7.3 /platform:x64 /warnaserror /target:exe ... GeometryTests.cs TubeGeometry.cs BoundaryRails.cs TubeLayoutSolver.cs` | 0 | 0 warnings, 0 errors |
| `GeometryTests.exe` execution | `GeometryTests.exe` | 0 | Passed `TestCylinders`, `TestRails`, `TestEqualPitchCustom`, `TestV3OverhangGeometry` (synthetic + curved/twisted/flip), and reference layout checks (`PASS all geometry tests`) |
| `ADDIN.csproj` **Debug\|x64** build | `MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /p:ReferencePath="C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist" /p:PostBuildEvent= /p:RegisterForComInterop=false /p:OutputPath="bin\CircleFace\Debug\\"` | 0 | Output: `bin\CircleFace\Debug\ADDIN.dll`; 0 warnings in `CircleFaceTubeLayout` |
| `ADDIN.csproj` **Release\|x64** build | `MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /p:ReferencePath="C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist" /p:PostBuildEvent= /p:RegisterForComInterop=false /p:OutputPath="bin\CircleFace\Release\\"` | 0 | Output: `bin\CircleFace\Release\ADDIN.dll`; 0 warnings in `CircleFaceTubeLayout` |

---

## 5. Real SOLIDWORKS 2024 Model Benchmarks & Timing (`V3BenchProbe.exe`)

Models tested:
- **4313 copy:** `...\scratch\copy_4313.SLDPRT` (solid sheet plate, `body_type=0`, `surface_type=4006`)
- **6866 copy:** `...\scratch\copy_6866.SLDPRT` (twisted surface sheet, `body_type=1`, `surface_type=4008`)
- Settings: `OD = 20 mm`, `M = 5 mm`, `EqualPitch`, `SmartOverhang`: `OFF` vs `ON (Hmax = 12 mm)`

| Model & Case | Baseline `523a760` (ms) | Optimized `9ad5e62` (ms) | Speedup | Result / Status | `H` (mm) | `effM` (mm) | `ext` / `maxExt` (mm) | Tries | Small / Large Pitch (mm) | Actual L/R Caps (mm) | Min Full Gap (mm) | Sheet Clashes | Lift (mm) |
|---|---:|---:|---:|---|---:|---:|---|---:|---|---|---:|---:|---:|
| `4313 OFF N=30` | — | 3,992 | — | OK | 0.000 | 0.000 | `False` / 0.000 | 0 | 27.1274 / 48.2417 | S `4.994/4.994` L `4.996/4.995` | 6.4657 | 0 | 0.0600 |
| `4313 V3_ON(12) N=30` | 5,051 | 3,914 | 1.29x | OK (baseline preserved) | 0.000 | 5.000 | `False` / 0.000 | 0 | 27.1274 / 48.2417 | S `4.994/4.994` L `4.996/4.995` | 6.4657 | 0 | 0.0600 |
| `4313 OFF N=40` | — | 5,025 | — | INFEASIBLE (`21 pair(s), first 1-2`) | 0.000 | 0.000 | `False` / 0.000 | 0 | 20.1716 / 35.8720 | S `4.994/4.994` L `4.996/4.995` | 0.0000 | 0 | 0.0600 |
| `4313 V3_ON(12) N=40` | 14,951 | 5,433 | **2.75x** | OK | **2.000** | -2.000 | `False` / 0.000 | 5 | 20.5317 / 36.2318 | S `-2.006/-2.006` L `-2.004/-2.005` | 0.0251 | 0 | 0.0600 |
| `4313 OFF N=41` | — | 5,217 | — | INFEASIBLE (`40 pair(s), first 1-2`) | 0.000 | 0.000 | `False` / 0.000 | 0 | 19.6673 / 34.9752 | S `4.994/4.994` L `4.996/4.995` | 0.0000 | 0 | 0.0600 |
| `4313 V3_ON(12) N=41` | 38,791 | 7,509 | **5.17x** | OK | **12.000** | -12.000 | **`True` / 2.248** | 25 | 20.5197 / 35.8270 | S `-12.007/-12.006` L `-12.005/-12.005` | 0.0234 | 0 | 0.0600 |
| `6866 OFF N=30` | — | 4,793 | — | OK | 0.000 | 0.000 | `False` / 0.000 | 0 | 27.1675 / 48.2913 | S `5.006/5.005` L `5.005/5.004` | 6.6668 | 0 | 0.1978 |
| `6866 V3_ON(12) N=30` | 4,682 | 4,733 | 0.99x | OK (baseline preserved) | 0.000 | 5.000 | `False` / 0.000 | 0 | 27.1675 / 48.2913 | S `5.006/5.005` L `5.005/5.004` | 6.6668 | 0 | 0.1978 |
| `6866 OFF N=40` | — | 6,205 | — | INFEASIBLE (`10 pair(s), first 30-31`) | 0.000 | 0.000 | `False` / 0.000 | 0 | 20.2015 / 35.9089 | S `5.006/5.005` L `5.005/5.004` | 0.0000 | 0 | 0.1978 |
| `6866 V3_ON(12) N=40` | 12,265 | 6,244 | **1.96x** | OK | **0.000** | 0.000 | `False` / 0.000 | 1 | 20.4571 / 36.1647 | S `0.006/0.005` L `0.005/0.004` | 0.0731 | 0 | 0.1420 |
| `6866 OFF N=41` | — | 6,173 | — | INFEASIBLE (`40 pair(s), first 1-2`) | 0.000 | 0.000 | `False` / 0.000 | 0 | 19.6965 / 35.0112 | S `5.006/5.005` L `5.005/5.004` | 0.0000 | 0 | 0.1978 |
| `6866 V3_ON(12) N=41` | 33,445 | 8,022 | **4.17x** | OK | **9.000** | -9.000 | `False` / 0.000 | 19 | 20.3943 / 35.7096 | S `-8.994/-8.995` L `-8.995/-8.996` | 0.0138 | 0 | 0.0600 |
| `6866_flip OFF N=30` | — | 4,763 | — | OK | 0.000 | 0.000 | `False` / 0.000 | 0 | 27.1619 / 48.2871 | S `4.994/4.994` L `4.995/4.996` | 6.4988 | 0 | 0.0600 |
| `6866_flip V3_ON(12) N=30` | 4,803 | 4,820 | 1.00x | OK (baseline preserved) | 0.000 | 5.000 | `False` / 0.000 | 0 | 27.1619 / 48.2871 | S `4.994/4.994` L `4.995/4.996` | 6.4988 | 0 | 0.0600 |
| `6866_flip OFF N=40` | — | 6,321 | — | INFEASIBLE (`19 pair(s), first 21-22`) | 0.000 | 0.000 | `False` / 0.000 | 0 | 20.1973 / 35.9058 | S `4.994/4.994` L `4.995/4.996` | 0.0000 | 0 | 0.0600 |
| `6866_flip V3_ON(12) N=40` | 16,010 | 6,680 | **2.40x** | OK | **1.500** | -1.500 | `False` / 0.000 | 4 | 20.5317 / 36.2399 | S `-1.506/-1.506` L `-1.505/-1.504` | 0.0244 | 0 | 0.0600 |
| `6866_flip OFF N=41` | — | 6,535 | — | INFEASIBLE (`40 pair(s), first 1-2`) | 0.000 | 0.000 | `False` / 0.000 | 0 | 19.6924 / 35.0081 | S `4.994/4.994` L `4.995/4.996` | 0.0000 | 0 | 0.0600 |
| `6866_flip V3_ON(12) N=41` | 39,106 | 8,985 | **4.35x** | OK | **11.500** | -11.500 | **`True` / 1.748** | 24 | 20.5197 / 35.8349 | S `-11.506/-11.507` L `-11.505/-11.505` | 0.0212 | 0 | 0.0600 |

---

## 6. UI, Cancel, Create, Undo, Planar, Assembly & Independent B-rep Audit (`V3FullIntegrationTest.exe`)

Command exit code: **`0`** (`PASS ALL V3 SOLIDWORKS 2024 INTEGRATION, UI, BREP, CREATE AND UNDO TESTS`).

Verified behaviors:
1. **UI Option Gating & Field Invalidation:**
   - Default opens with `Auto count` checked; `Small-end mode`, `Allow symmetric overhang (V3)`, `Max cap overhang`, and `Create 3D Sketch` disabled.
   - Selecting `Custom` + `Touch at small end` leaves `Allow symmetric overhang (V3)` disabled.
   - Selecting `Custom` + `Equal pitch` enables `Allow symmetric overhang (V3)` (default OFF); checking it enables `Max cap overhang` (default `12 mm`, clamped dynamically to `OD`, e.g., lowering `OD` to `10` clamps `MaxOverhang` to `10`).
   - Switching back to `Auto count` disables `Allow symmetric overhang` and enforces `TangentContact` `N=41` on `6866` (`SmartOverhangEnabled=false`).
   - Changing `Flip Side`, `Max cap overhang`, etc. immediately clears preview, clears `Accepted`, and locks `Create 3D Sketch`.
2. **OK & FAIL Paths + Independent Full-Cylinder B-rep Proof:**
   - `4313 N=30 V3_ON(12)`: keeps baseline `M=5.000 mm`, `H=0`, `attempts=0`; report states `baseline margin 5.000 mm preserved`.
   - `4313 N=40 V3_ON(12)`: `H=2.000 mm`, `minFull=0.0251 mm`, `clashes=0`, `Create` enabled.
   - `4313 N=41 V3_ON(Hmax=8)` (**FAIL case**): fails after 17 trials (`11` interfering pairs at `H=8.000 mm`), `Create` locked, report shows `INFEASIBLE:` and `BREP: Skipped (geometry invalid)` with zero `NaN` or misleading `BREP: Clear` text.
   - `4313 N=41 Flip=True V3_ON(12)` (**FAIL case on solid**): rejects cleanly in `39,300 ms`; pair check clears at `h=9.500 mm` (`attempt 20`), 9 lift passes confirm all 41 tubes clash into the solid (`sheet_clashes=41`), `SMART_OVERHANG_STOP reason=flipped_solid_material_clash` halts further overhang steps cleanly, `Create` stays locked.
   - `4313 N=41 V3_ON(12)` (**OK extrapolated case**): `H=12.000 mm`, `ext=True`, `maxExt=2.248 mm`, `minFull=0.0234 mm`, `clashes=0`. Independent SolidWorks `CreateBodyFromCyl` + `Operations2(SWBODYINTERSECT)` verified **0 intersections** across all 41 full-length cylinders and the `4313` solid plate.
   - `6866 Flip=True N=41 V3_ON(12)` (**OK flipped twisted extrapolated case**, tested with `40 + Add +1`): `H=11.500 mm`, `maxExt=1.748 mm`, `minFull=0.0212 mm`, `clashes=0`. Independent SolidWorks `CreateBodyFromCyl` + `SWBODYINTERSECT` verified **0 intersections** across all 41 cylinders and the `6866` surface body.
3. **Preview, Cancel, Create 3D Sketch, Undo & Planar/Assembly Flows:**
   - Preview followed by Cancel on `4313` added `0` features to the Part.
   - `CreateSketch` on `4313` (`N=41`) created 3D sketch `TUBE_AXES_3D_41` containing 41 construction lines (`UndoSketch` restored initial feature count cleanly).
   - `CreateSketch` on `6866` (`Flip=True, N=41`) created `TUBE_AXES_3D_41` with 41 construction lines and `UndoSketch` restored initial feature count.
   - Planar curved sector (`surface_type=4001`, `area_m2=1.2018`, `R=3247 L=811.35 -> R=4323.16 L=1421.97`): `N=30 -> H=0.000 mm`, `N=40 -> H=0.000 mm`, `N=41 -> H=9.000 mm` (`maxExt=0.000 mm`).
   - Assembly `copy_4316.SLDASM` (`N=41, H=12.000 mm`): mapped assembly face to component Part, created `TUBE_AXES_3D_41` with 41 construction lines verified in assembly document coordinates, and verified `UndoSketch`.

---

## 7. Assembly Multi-Component Collision Audit (`AssemblyCompClashProbe.exe`)

Following `V3_SMART_OVERHANG_IMPLEMENTATION.md` checklist item 5, `AssemblyCompClashProbe.exe` tested all 57 components of `copy_4316.SLDASM` against the transformed `4313` tube cylinders:
- **`N=30` (`H=0.000 mm`, V2 baseline):** `axis=30` intersects solid component `4316-04-KDA1-o1-1` (1 hit).
- **`N=40` (`H=2.000 mm`, `ext=False`):** `axis=40` intersects `4316-04-KDA1-o1-1` (1 hit).
- **`N=41` (`H=12.000 mm`, `ext=True`, `maxExt=2.248 mm`):** `axis=40` and `axis=41` intersect `4316-04-KDA1-o1-1` (2 hits).

This confirms that checking adjacent components across a multi-part assembly remains outside the single-body sheet checker and must be reviewed with SolidWorks Interference Detection when placing tubes in assembly context.

---

## 8. Debug Log Artifacts

Inspected full V3 integration logs containing `SMART_OVERHANG`, `SMART_OVERHANG_TRIAL`, `SMART_OVERHANG_RESULT`, `OVERHANG_EXTRAPOLATED`, `SMART_OVERHANG_ACCEPT`, `SMART_OVERHANG_STOP`, `PREVIEW_SUMMARY`, `SMART_OVERHANG_SUMMARY`, `SHEET`, `AXIS`, `COMMIT`, and `UNDO`:
- `C:\Users\SGN26\AppData\Local\TAI_TOOL\CircleFaceTubeLayout\TUBE_FACE_V3_4313_20261117_225433_590.log` (84,356 lines — covers `4313` OK `N=30`, OK `N=40 H=2`, FAIL `N=41 Hmax=8`, FAIL `N=41 Flip=True`, OK `N=41 H=12`, Create `TUBE_AXES_3D_41`, Undo)
- `C:\Users\SGN26\AppData\Local\TAI_TOOL\CircleFaceTubeLayout\TUBE_FACE_V3_6866_20261117_225554_838.log` (28,039 lines — covers `6866` Auto `TangentContact N=41`, OK `Flip=True N=41 H=11.5`, Create `TUBE_AXES_3D_41`, Undo)

---

## 9. Limitations & Remaining Items for Codex Independent Review

1. **Multi-component assembly interference:** `SwFaceLayout.CheckSheet` checks against `ownerBody` (the selected face's Part body) and tube-tube GJK only. On `4316.SLDASM`, neighboring component `4316-04-KDA1-o1-1` intersects outer tubes even at `N=30, M=5 mm`.
2. **Tangent continuation vs true untrimmed surface:** Beyond trimmed boundaries `[0, L]`, `ExtendRail` uses linear tangent extrapolation with frozen endpoint normals (bounded to `center_ext <= D/2`). As tested up to `maxExt=2.248 mm`, `ownerBody` B-rep clearance held, though B-rep does not model unmodeled neighboring parts.
3. **Flip Side on thick solid plates:** Even with the `flipped_solid_material_clash` early stop after the first pair-clear `h`, that failing `h` runs 9 lift passes × `N` B-rep booleans (~39 s on `4313` `N=41`) before rejecting cleanly.
4. **Tangency disclaimer:** Generated 3D construction axes connect offset endpoints along local surface normals and verify zero detected tube/sheet B-rep intersections; a straight tube is not guaranteed globally tangent to an arbitrary doubly curved/twisted surface at interior points.
5. **Merge readiness:** PR `#65` remains Draft and unmerged into PR `#64` or `main` pending independent Codex review.
