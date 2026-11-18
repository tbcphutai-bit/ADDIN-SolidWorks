# CODEX INDEPENDENT REVIEW REPORT — CIRCLE ALONG FACE V3 (PR #65)

- **Repository:** `https://github.com/tbcphutai-bit/ADDIN-SolidWorks`
- **Pull Request Reviewed:** `#65` (`[DRAFT] Circle Along Face V3 — opt-in Smart Symmetric Overhang`) — Draft, unmerged
- **Branch:** `feature/circle-face-v3-smart-overhang-20261117`
- **Base Branch (PR #64):** `feature/circle-face-custom-equal-both-ends-20261117` (`782a84d18d423c97da235690a5018aa8d48b7987`)
- **Handoff & Baseline Reviewed:** `57a0048` (`9ad5e62` + `CODEX_V3_INDEPENDENT_REVIEW_HANDOFF.md`)
- **Review Fix & Verification Commit:** `8faca6e61251912a338b0396da1ddc7c4d23eceb` (`8faca6e`)
- **Environment Independently Executed:** Windows x64, .NET Framework 4.8, C# 7.3 (`Roslyn csc.exe` + `MSBuild.exe`), SOLIDWORKS 2024 SP5.0 (`rev 32.5.0`)

---

## 1. Executive Verdict

Independent code audit and live SOLIDWORKS 2024 SP5.0 verification of PR `#65` confirmed the core V3 overhang geometry on standalone single-body parts while uncovering **four actionable engineering gaps (1 P0, 1 P1, 2 P2)** in baseline `57a0048` / `9ad5e62`:
1. **P0 — Unchecked external assembly & multi-body collisions (`4316.SLDASM`):** `CheckSheet` only tested the selected face's owning body; a buggy probe (`Cyl` mutated across `Operations2` without `.Copy()`) had also masked 40 other tube bodies and 47 surface offset sheets in `4316.SLDASM`. Furthermore, standalone `copy_4313.SLDPRT` contains a visible modeling helper surface `'ｻｰﾌｪｽ-延長2'` (`type=1`) overlapping the bend zone alongside hidden solid `'ﾛﾌﾄ ﾍﾞﾝﾄﾞ1'`.
2. **P1 — Geometric-only pair clearance (`0.0002 mm` GJK bound vs fabrication tolerance):** Refined `N=41` layouts clear by `0.0004–0.0234 mm` (14–23 µm before refinement; 0.4–2.5 µm after refinement), which proves CAD separation only—not shop-floor fit-up/bend tolerance.
3. **P2 — Unproved `flipped_solid_material_clash` early pruning:** Constructed a real SOLIDWORKS 2024 counterexample (tapered `0.25 mm` flange with a `5 mm` central backing rib) where `Flip=True, N=2, M=5 mm` clashes on **2/2 (`all N`) tubes**, yet increasing overhang clears cleanly at **`H = 5.800 mm`**.
4. **P2 — Coarse `0.5 mm` overhang grid & `H = D` cap clamp edge case:** `0.5 mm` steps leave up to ~0.45 mm excess protrusion (`N=41` clears at `11.60 mm` rather than `12.00 mm`), and `target_cap = -20.0 mm` (`H = D`) fails station tolerance because oblique cap correction pushes nominal center station past `-D/2`.

All **P0, P1, and P2 code fixes** have been implemented cleanly within `CircleFaceTubeLayout` in commit **`8faca6e`**, compiled with 0 errors and 0 command warnings, and verified end-to-end in SOLIDWORKS 2024 SP5.0. PR `#65` remains **Draft** and unmerged pending human CAD designer sign-off on the two default policy settings in Section 7.

---

## 2. Severity-Ranked Findings, Fixes & Proofs

### [P0] Assembly & Multi-Body Collision Validation + Unambiguous Scope Reporting — FIXED (`8faca6e`)
- **Independent Findings on `4316.SLDASM` & `4313.SLDPRT`:**
  - `CodexAuditProbe.exe` (`task-458`), `List4316Comps.exe` (`task-468`), and `Inspect3d1.exe` (`task-559`) inspected all **57 components (89 visible external bodies)** of `copy_4316.SLDASM`:
    * **`4316-04-KDA1-o1-1, -3..-42` (41 components, `type=0` solid):** Existing modeled φ20 tube components seated directly on `4313-04-HU-H1-1`. Proper non-mutating `.Copy()` B-rep intersection checks reproduce heavy collisions (`140,000–227,000 mm³`) on **`Auto_Tangent`**, **`Custom_N20_M5`**, **`Custom_N30_M5`**, **`V3_N40_H12`**, and **`V3_N41_H12`**.
    * **`4316-04-KDA1-3d-1` (1 component, `47` bodies, `type=1` sheet):** Visible 3D offset surface bodies (`ｻｰﾌｪｽ-ｵﾌｾｯﾄ1..47`) overlapping the tube zone.
    * **`4316-04-KDA1-bs1-1` (1 component, `1` body, `type=0` solid):** Backing structural base plate under `4313`; `0` collisions with `N=30/40/41`.
  - On standalone `copy_4313.SLDPRT` (`Check4313Bodies.exe`, `task-518` & `task-520`), two bodies exist: hidden solid `'ﾛﾌﾄ ﾍﾞﾝﾄﾞ1'` (`visible=False`, owning the selected face) and visible modeling helper surface `'ｻｰﾌｪｽ-延長2'` (`type=1`, `visible=True`, overlapping `30/30` tubes in `N=30` if mistakenly treated as a physical obstacle).
- **Resolution in `8faca6e` (`SwFaceLayout.cs`, `CircleFaceTubeLayoutCommand.cs`, `CircleFaceTubeLayoutForm.cs`, `TubeLayoutSolver.cs`):**
  - Added `ExternalBodyContext` (transient `.ICopy()`/`.Copy()` bodies transformed via rigid `Component2.Transform2`, cached bounding boxes with `+0.5 mm` AABB prefilter, zero modification to original Part/Assembly models, full COM disposal).
  - Evaluates all other visible unsuppressed solid/surface assembly components and other solid bodies in multi-body Parts; flags lightweight, unresolved, or non-rigid assembly components as inconclusive (`ContextInconclusiveCount`) so uninspected components can never silently report as verified clear.
  - Excludes same-Part construction `swSheetBody` surfaces (like `ｻｰﾌｪｽ-延長2` on solid `4313`) while logging `CONTEXT_SURFACE_EXCLUDED`.
  - Added UI checkbox **`Check other assembly/part bodies`** — **default `Checked = true`** whenever external bodies exist (`89 found` in `4316.SLDASM`), disabled on single-body parts (`none in Part`).
  - When checked and a collision occurs, `Create 3D Sketch` stays **locked** and Preview/log name colliding bodies (`ASSEMBLY_BREP`). When explicitly unchecked by the designer (e.g. creating a replacement sketch while mockup parts remain visible), `Create` is enabled **only** with unmistakable stamps:
    `Scope: LIMITED — 89 other body/component(s) NOT checked (assembly interference unverified).`
    and `OK (LIMITED SCOPE — other assembly/part bodies UNVERIFIED): ...` + log `CONTEXT_CHECK skipped=true`.

### [P1] Configurable Fabrication Pair Clearance (`Min pair gap`) & Quantitative Trade-Off — FIXED (`8faca6e`)
- **Quantitative Sweep (`CodexAuditProbe.exe`, `OD=20 mm, M=5 mm`, Fixed `H` via `AnalyzeOne`):**

| Model & `N` | `H = 2.0` / `9.0` (first `0.5` OK) | `H = 11.5 mm` | `H = 12.0 mm` | `H = 14.0 mm` | `H = 16.0 mm` | `H = 18.0 mm` | `H = 20.0 mm` (`= OD`) |
|---|---|---|---|---|---|---|---|
| `4313 N=40` | `0.0251 mm` (at `H=2`) | `0.5105 mm` | `0.5372 mm` | `0.6440 mm` | `0.7507 mm` | `0.8533 mm` | **FAIL** (cap `-D/2` clamp, `bad=0`) |
| `4313 N=41` | FAIL (`bad=8` at `H=9`) | FAIL (`bad=1`) | `0.0234 mm` (`ext 2.248`) | `0.1276 mm` (`ext 4.248`) | `0.2317 mm` (`ext 6.248`) | `0.3309 mm` (`ext 8.248`) | **FAIL** (cap `-D/2` clamp, `bad=0`) |
| `6866 N=41` | `0.0138 mm` (at `H=9`) | `0.1318 mm` | `0.1550 mm` | `0.2477 mm` (`ext 4.240`) | `0.3404 mm` | `0.4332 mm` | **FAIL** (cap `-D/2` clamp, `bad=0`) |
| `6866_flip N=41` | FAIL (`bad=6` at `H=9`) | `0.0212 mm` (`ext 1.748`) | `0.0473 mm` | `0.1515 mm` | `0.2557 mm` (`ext 6.248`) | `0.3550 mm` | **FAIL** (cap `-D/2` clamp, `bad=0`) |

- **Engineering Analysis:** Each `+1.0 mm` of symmetric cap overhang distributes `+2.0 mm / 40 ≈ +0.052 mm` of additional 3D clearance per pitch at `N=41`. Within `center_ext <= D/2`, maximum achievable `minFull` on `4313 N=41` is `~0.33 mm` (`H = 18 mm`). Setting a hardcoded `>= 0.50 mm` limit would falsely block `N=41`.
- **Resolution in `8faca6e` (`TubeLayoutSolver.cs`, `SwFaceLayout.cs`, `CircleFaceTubeLayoutForm.cs`, `GeometryTests.cs`):**
  - Added **`Min pair gap in EqualPitch (mm)`** (`default = 0.00 mm` = pure geometric proof `> 0.0002 mm`, disabled in `TangentContact` which retains `TargetGap = 0.005 mm`).
  - Whenever `EqualPitch` succeeds with `MinimumGap < 0.10 mm`, Preview emits `[Tight geometric gap < 0.10 mm: verify fabrication tolerance]`.
  - Verified on `4313 N=41` with `Min pair gap = 0.20 mm`: cleanly **rejected** at `Hmax = 12 mm`, and **verified OK** at `Hmax = 16 mm` (`refinedH = 15.400 mm`, `maxExt = 5.648 mm`, `minFull = 0.2005 mm`). Practical shop recommendation: target **`>= 0.20 mm`** (or minimum `0.10 mm`) when end extension allows.

### [P2] Local Bisection Overhang Refinement (`~0.05 mm`) & Honest Grid Reporting — FIXED (`8faca6e`)
- `SwFaceLayout.Analyze()` now locates the first passing bracket `(lastFailH, firstPassH)` on the `0.5 mm` scan, then runs up to 3 bisection steps snapped to `0.05 mm` with full GJK + B-rep checks preserved (`SMART_OVERHANG_REFINE`). UI states `(0.5 mm scan + 0.05 mm local refinement; first feasible)` rather than claiming a global minimum.
- Also documents why `target_cap = -D` (`20.0 mm`) fails cap tolerance when end tilt pushes station offset past `-radius`: `-radius` (`center_ext <= D/2`) clamp is intentionally retained for geometric safety.

### [P2] Removal of Unproved `flipped_solid_material_clash` Pruning & Speed Improvement — FIXED (`8faca6e`)
- **Counterexample Proven in SOLIDWORKS 2024 (`TestFlippedRibbedSolidCounterexample`):**
  - Built a tapered `40 -> 56 mm` × `200 mm` plate (`0.25 mm` flange + `5 mm` central backing rib across `X = [-8..+8 mm]`).
  - With `Flip=True, N=2, M=5 mm`, **baseline had `clashes = 2/2` (`all N`)** across all 9 lift passes (the old pruning stopped immediately).
  - With `8faca6e` (exhaustive over `0..Hmax`), V3 found and verified **`H = 5.800 mm` (`clashes = 0`, `lift = 1.253 mm`, `Issue = null`)**.
- **Lossless Optimization Replacing Pruning:** In `AnalyzeOne` (`skipSheetOnGeometryFail == true`), B-rep `CheckSheet` starts at `startAxis`, aborts the lift pass on the first `Intersecting`/`Inconclusive` tube, and defers all-axis `EstimateFaceClearance` until B-rep clears (evaluating only the single clashing axis to step `lift` on failed passes). Exhaustive `4313 N=41 Flip=True` (`0..12.0 mm`) completed in **`18,114 ms`** (`clashes = 41/41`), eliminating the `150,443 ms` unpruned stall while remaining **2.17x faster than `9ad5e62`'s pruned `39,300 ms`** and 100% exhaustive.

### [P2] Tangency Scope Clarity — FIXED (`8faca6e`)
- Preview scope and OK lines explicitly clarify that axes use offset end normals and do not guarantee continuous tangency across twisted interior surfaces.

---

## 3. Independent Verification Summary (Post-`8faca6e`)

| Suite / Check | Environment & Scope | Exit Code | Key Verified Results |
|---|---|---:|---|
| `GeometryTests.exe` (`csc /langversion:7.3 /platform:x64 /warnaserror`) | Standalone pure geometry + `minClearance=0.20mm` + curved/twisted/flip V3 regressions | **0** | 0 warnings/errors; `PASS all geometry tests` |
| `MSBuild ADDIN.csproj` **Debug\|x64** | .NET 4.8 + SW 2024 SP5.0 Interop (`bin\CircleFace\Debug\ADDIN.dll`) | **0** | 0 warnings in `CircleFaceTubeLayout` |
| `MSBuild ADDIN.csproj` **Release\|x64** | .NET 4.8 + SW 2024 SP5.0 Interop (`bin\CircleFace\Release\ADDIN.dll`) | **0** | 0 warnings in `CircleFaceTubeLayout` |
| `CodexV3VerificationTest.exe` (`task-577`) — Section 1 (`4313` Part) | `N=30/40/41`, `minGap=0.20mm`, exhaustive `Flip=True`, 41-cyl `SWBODYINTERSECT` | **0** | `N=40`: `16,545 ms`, `refinedH=1.550 mm` (`minFull=0.0024`); `N=41`: `14,994 ms`, `refinedH=11.600 mm`, `maxExt=1.848 mm` (`minFull=0.0025`, **0 SW B-rep overlaps**); `N=41 minGap=0.20 Hmax=12`: **LOCKED**; `Hmax=16`: **OK** `15.400 mm` (`minFull=0.2005`); `Flip=True N=41`: `18,114 ms`, exhaustive `41/41` clashes |
| `CodexV3VerificationTest.exe` (`task-577`) — Section 2 (`6866` Surface) | `Auto`, `N=41` normal & `Flip=True`, 41-cyl `SWBODYINTERSECT` | **0** | `Auto N=41 OK`; `EqualPitch N=41`: `refinedH=8.750 mm` (`minFull=0.0015`); `Flip N=41`: `refinedH=11.100 mm` (`maxExt=1.348 mm`, `minFull=0.0004`, **0 SW B-rep overlaps**) |
| `CodexV3VerificationTest.exe` (`task-577`) — Section 3 (Multi-Body Part) | Tapered `80 -> 140 mm` solid + 2nd solid bracket (`Merge=false`) | **0** | `CheckOtherBodies=ON` blocked (`1` clash vs 2nd body); `OFF` enabled `Create` with explicit `LIMITED SCOPE — UNVERIFIED` |
| `CodexV3VerificationTest.exe` (`task-577`) — Section 4 (Flip Counterexample) | Tapered `0.25 mm` flange + `5 mm` central backing rib, `Flip=True, N=2` | **0** | Baseline `M=5` had `2/2` clashes; exhaustive V3 cleared cleanly at `H=5.800 mm` (`0` clashes) |
| `CodexV3VerificationTest.exe` (`task-577`) — Section 5 (`4316.SLDASM`) | Real 57-comp (`89` ext bodies) assembly policies | **0** | `CheckBodies=ON` reproduced `KDA1-o1-*` & `KDA1-3d-1` clashes on `Auto`, `N=30`, `N=40`, `N=41` (Create locked); `OFF` stamped `LIMITED SCOPE`; with mockup tubes & `3d-1` surfaces hidden leaving `bs1-1` (`1` body), `CheckBodies=ON` passed (`H=11.600 mm`, Create & Undo `TUBE_AXES_3D_41`) |
| `CodexExtraRegressionCheck.exe` (`task-585`) | `Add +1`, live Preview display/hide, field invalidation, `Cancel`, `6866` Flip Create/Undo, Planar curved sector | **0** | `4313` Preview+Cancel left 0 features; `6866 Flip` Create & Undo (`TUBE_AXES_3D_41`) OK; Planar sector: `Auto=41`, `N=30 H=0`, `N=40 H=0`, `N=41 refinedH=8.600 mm` (`minFull=0.0021`) |

---

## 4. Modified Files (`8faca6e` within PR #65 Scope)

Only files under `Commands/CircleFaceTubeLayout/`, `Tests/CircleFaceTubeLayout/`, and `Prototypes/CircleFaceTubeLayout/` are modified in `origin/feature/circle-face-custom-equal-both-ends-20261117..8faca6e`:
- `Commands/CircleFaceTubeLayout/CircleFaceTubeLayoutCommand.cs`
- `Commands/CircleFaceTubeLayout/CircleFaceTubeLayoutForm.cs`
- `Commands/CircleFaceTubeLayout/SwFaceLayout.cs`
- `Commands/CircleFaceTubeLayout/TubeLayoutSolver.cs`
- `Tests/CircleFaceTubeLayout/GeometryTests.cs`
- `Prototypes/CircleFaceTubeLayout/V3_SMART_OVERHANG_IMPLEMENTATION.md`
- `Prototypes/CircleFaceTubeLayout/V3_IMPLEMENTATION_REPORT.md`
- `Prototypes/CircleFaceTubeLayout/CODEX_V3_INDEPENDENT_REVIEW_HANDOFF.md`
- `Prototypes/CircleFaceTubeLayout/CODEX_V3_REVIEW_REPORT.md` (this report)

No unrelated add-in commands or `.csproj` files were changed.

---

## 5. Observed Debug Logs

- `C:\Users\SGN26\AppData\Local\TAI_TOOL\CircleFaceTubeLayout\TUBE_FACE_CODEX_4313_20261118_072127_757.log` (`SMART_OVERHANG_REFINE`, `min_pair_gap_mm=0.200`, exhaustive `Flip=True`)
- `C:\Users\SGN26\AppData\Local\TAI_TOOL\CircleFaceTubeLayout\TUBE_FACE_CODEX_4316_ASM_20261118_072343_593.log` (`CONTEXT_INIT bodies=89`, `ASSEMBLY_BREP`, `CONTEXT_CHECK skipped=true`, `bs1-1` clear `COMMIT`/`UNDO`)

---

## 6. Remaining Limitations

1. **Construction surfaces in multi-surface Assemblies vs same-Part helpers:** `ExternalBodyContext` excludes `swSheetBody` helper surfaces belonging to the *selected* solid Part, while still checking visible `swSheetBody` surfaces belonging to *other* Assembly components (e.g., `4316-04-KDA1-3d-1`). Users generating replacement axes in Assemblies where mockup tube/surface components remain visible must either hide reference mockup components or uncheck `Check other assembly/part bodies` (explicitly accepting `LIMITED SCOPE`).
2. **Global minimum overhang not guaranteed on non-monotone geometry:** Local bisection refines only the first passing `0.5 mm` bracket (`~0.05 mm` resolution); disjoint lower islands are not searched if an intermediate point clears, and if `center_ext == D/2` is approached (`target_cap == -D`), cap obliquity prevents hitting exact `-D`.
3. **Sub-micron refined gaps when `Min pair gap == 0`:** Because bisection tightly approaches the collision boundary (e.g. `0.0004–0.0025 mm`), leaving `Min pair gap == 0.00 mm` relies on user discretion via the `< 0.10 mm` tight-gap warning unless a positive fabrication minimum (e.g. `0.20 mm`) is configured.

---

## 7. Release Recommendation

**Conditional Ready for Designer Sign-Off (Keep Draft Until Policy Confirmed; Do Not Auto-Merge Yet).**  
Code quality, P0 assembly/multi-body B-rep safety, P1 configurable clearance, P2 search refinement, exhaustive flip safety, and all SOLIDWORKS 2024 regression checks pass cleanly at **`8faca6e`**. Before merging PR `#64` and PR `#65` into `main`, confirm two UX/shop-floor policy defaults with the CAD owner:
1. Keep **`Check other assembly/part bodies` default = `ON`** with explicit `LIMITED SCOPE` opt-out checkbox (recommended for safety).
2. Keep **`Min pair gap` default = `0.00 mm`** (backward-compatible with V3 `Hmax=12` acceptance on `4313 N=41`, with UI tight-gap alert below `0.10 mm`) vs raising the UI default to **`0.20 mm`** (which requires `Hmax >= 16 mm` / `H = 15.40 mm` on `4313 N=41`).
