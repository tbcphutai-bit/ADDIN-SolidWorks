# Runbook 43 — Auto Frame Joint: REAL 3D axis/joint geometry solver (read-only)

**CODE OWNER: ChatGPT. Anti: only copy/paste the exact committed C# into the currently open Visual Studio project. No self-authored backend changes. NO BUILD, REBUILD, F5, DLL/PDB DEPLOY.**

## Goal

Build on the working PMP face/edge selection and A→B auto-focus and Runbook 42 automatic read-only Analyze:
- Compute the **3D acute angle** of estimated frame axes (not the signed/interior corner angle).
- Compute the **closest points on two actual FINITE centerline segments** in Assembly coordinates, in meters internally and millimeters in UI/report.
- Distinguish nearest infinite lines outside a Part's measured length from finite-axis proximity.
- Compute a **candidate XYZ location**, measured 3D gap between estimated axes (NOT material clearance), vertex-sampled world envelope intersection CANDIDATE, and two theoretical axis-bisector normal candidates (NOT final cut planes).
- Display these values directly inside native PropertyManager Page so the user can visually validate two selected Frame profiles; write detailed report to Visual Studio Output.

**CRITICAL DESIGN DOMAIN CONTEXT**:
- The actual user-supplied 1618a and 3615a examples show Web(bottom), Wall and Lip(top protrusion) having DIFFERENT cut boundaries. Never treat a theoretical bisector/45-degree miter as a production cut.
- KCC 26003: where it APPLIES, short edge wins and long edge loses; special drawing overrides this; equal lengths require direction clarification. Whether this rule applies to a particular frame pair is NOT verified by generic Geometry B-rep. Long/short overall Part length alone is not proof of winning face/cut.
- If axes are skew, out of finite span, close parallel, or the inferred axis has weak confidence, report geometry clearly as uncertain. Do not cut.
- Vertex-sampled XYZ "envelope" can miss curved extremities and can false-positive overlap. It is NOT a solid-solid interference test. SOLIDWORKS native GetBox/GetBodyBox are themselves approximate (see API docs); do not make collision/trim decisions from this.

## Source authored

New GitHub branch: `feat/afj-3d-joint-geometry-calculation-20261021`.
Base: `ce4d5d484a7d7156b131f5ad0f5db7e86563c48e` (Runbook 42).

**Exactly five runtime source/project file changes:**

1. **NEW** `Commands/AutoFrameJoint/AutoFrameJointGeometryMath.cs` — pure C# 7.3 3D geometry math, no SOLIDWORKS or COM calls. Computes closest distance between finite axis segments by checking unconstrained infinite-line solution and segment boundaries, handles skew, perpendicular and near-parallel axes.
2. **MODIFIED** `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs` — preserves the existing B-rep edge axis extractor, now stores centroid in Assembly coordinates, measured axis segment interval and vertex-sampled envelope, calls the pure solver, emits numerical results and explicit engineering limitations.
3. **MODIFIED** `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs` — parses the completed full report ONCE (no repeated B-rep scan) and returns 6 short fields to the PropertyManager.
4. **MODIFIED** `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` — adds a native PMP group `Tính giao góc 3D (ước lượng)` with separate Angle, XYZ, axis gap, relation and long/short labels; keeps status and preserves geometry labels after explicit Ghost preview.
5. **MODIFIED** `ADDIN.csproj` — adds exactly ONE `<Compile Include="Commands\AutoFrameJoint\AutoFrameJointGeometryMath.cs" />`. Existing Debug|x64 output remains **`C:\Tao moi\`**, with empty Pre/PostBuild.

**Backend NOT touched:** `AutoFrameJointGhostPreview.cs`, `AutoFrameJointGhostSessionManager.cs`, `BomTaskPaneControl.cs`, `BomCommandLayout.cs`, `BomTaskPaneControl.Designer.cs`, Check Hole, Check chấn, Mirror, DXF, miter feature creation, assemblies or Parts. No actual Cut/Save is implemented.

## Full output lines for developers

```text
JOINT_STATUS: FINITE_AXES_NEAREST | OUTSIDE_ONE_OR_BOTH_FRAME_SPANS | PARALLEL_OR_NEAR_PARALLEL
JOINT_ANGLE_DEG: ...
JOINT_XYZ_MM: (...)
JOINT_NEAREST_A_MM: (...)
JOINT_NEAREST_B_MM: (...)
JOINT_FINITE_AXIS_GAP_MM: ...
JOINT_INFINITE_AXIS_GAP_MM: ...
JOINT_NEAR_POINTS_INSIDE_SPANS: YES | NO
JOINT_VERTEX_ENVELOPE_OVERLAP_CANDIDATE: YES | NO
JOINT_VERTEX_ENVELOPE_SIZE_MM: (...) [if candidate]
JOINT_BISECTOR_NORMAL_CANDIDATE_1: (...)
JOINT_BISECTOR_NORMAL_CANDIDATE_2: (...)
JOINT_GEOMETRY_NOTE: Estimated centroid axes and vertex envelopes, NOT exact solid intersection.
JOINT_KCC26003: SHORT_WINS_LONG_IF_APPLICABLE; DRAWING_OVERRIDE_FIRST; APPLICABILITY_NOT_VERIFIED.
Winner/Loser: UNCONFIRMED for this Assembly.
Profile Web/Wall/Lip: NOT YET ANALYZED.
Cut geometry: NOT GENERATED. No SolidWorks document was changed.
```

The reported XYZ is the midpoint of the closest FINITE axes, not necessarily the place where two physical channel sections contact. This is correct for preliminary alignment but NOT a final CNC cut datum.

## Antigravity implementation instructions — exact copy into original project

**TARGET**: `C:\Tao moi\ADDIN\ADDIN.csproj`.

1. Check `git status -sb`, `git rev-parse HEAD`, `git diff --stat` and relevant `git hash-object` from the REAL project in Visual Studio (NOT a separate unused worktree).
2. Before overwrite confirm the existing local files match the expected Runbook 42 baseline:
   - `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` = `aa3a7c77fba634bb2e3f32db29daeea36c57cb9f`
   - `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs` = `f2700438cff8b7f3537bb1eab513dda1fa7b7956`
   - `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs` = `e0bf7b1d2c76dbb73a08cc2e2b0f515c19400d70`
   - `ADDIN.csproj` = `3aa1d7c277640cfd22b22ed511013a094505bec3`.
3. If ANY existing target differs, STOP and send exact diff; ChatGPT will author conflict-specific reconciliation. DO NOT overwrite unrelated work.
4. If ALL match, copy the exact THREE updated C# files AND the ONE new Math C# file from the committed GitHub branch. Apply exact `ADDIN.csproj` content (one extra Compile Include only). **No code generation by Anti.**
5. Verify new Git blob hashes match committed source exactly; report `git diff --stat`, `git status -sb` and 5 hashes. Do not change other source.
6. DO NOT build, rebuild, F5, deploy or re-register; leave SOLIDWORKS running and DLL/PDB completely untouched. User will build manually when ready.

## Mandatory manual acceptance TESTS after user-approved Rebuild

Safeguard installed DLL/PDB pair from `C:\Tao moi\` before manual Debug|x64 Rebuild; this config writes directly into that directory. End any conflicting Visual Studio/SOLIDWORKS debugging before Build.

- Standard orthogonal frame pair (example user screenshot): after A/B selection, native PMP must show 90° axis angle, candidate XYZ in mm, nearest-axis gap, span relation, and role; Output must contain full `JOINT_*` detail. Do not assume gap=0 for asymmetric C/U channels.
- A/B reversed: geometric location and unsigned angle should be physically unchanged, but long/short labeling swaps between A/B.
- Two parallel separated frames: status `PARALLEL_OR_NEAR_PARALLEL`, no miter plane suggestion.
- Two skew axes separated in Z: show positive axis gap in mm; do not falsely claim physical contact or choose a cut.
- Two finite members whose INFINITE axes would cross beyond their ends: status `OUTSIDE_ONE_OR_BOTH_FRAME_SPANS`, finite nearest XYZ is different from infinite crossing.
- A second Part configuration, invalid/lw/two-body cases: retain `NEEDS_REVIEW`, never change configuration or resolve automatically.
- Runbook 42: auto-analyze when Frame B accepted; manual Phân tích still refreshes; explicit `Xem trạng thái A/B` still displays current geometry ONLY; after Cancel all Ghost overlays removed.

**Pure math reference fixtures**:
- perpendicular centered X/Y axis segments length 2m each: angle=90°, nearest gap=0, closest XYZ=(0,0,0).
- same example with Y line at z=0.02m: infinite/finite axes gap=20mm, DO NOT infer material collision from axis gap.
- near-parallel X/X axes with 10mm offset: status PARALLEL_OR_NEAR_PARALLEL, no candidate bisector.
- perpendicular B axis relocated to x=2m while A is limited to [-1,+1]m: status outside finite span.

No mechanical manufacturing tolerance (0.01/0.5mm), lip cut-off, wall flush, winner cut decision, actual trim feature, file Save or persistent Part modifications have been invented. Any of these need a profile-specific validated cut rule and a later safe geometry operation.

**Status: code authored and Git blobs verified statically; NOT YET BUILT, TESTED IN HOST, NOR CUTTING.**
