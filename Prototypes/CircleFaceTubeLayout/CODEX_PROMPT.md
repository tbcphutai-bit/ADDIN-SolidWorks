# CODEX TASK — Circle Along Face / Twisted Sheet Tube Layout (SolidWorks 2024)

**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`
**Work branch:** `feature/circle-face-tube-layout-prototype-20261110`
**Status:** EXPERIMENTAL / NOT COMPILED / NOT TESTED ON SOLIDWORKS. No PR merged.
**Reference sources:** `Prototypes/CircleFaceTubeLayout/Commands/CircleFaceTubeLayoutCommand.cs` and `CircleFaceTubeLayoutForm.cs`.

## Your assignment
FIRST study this repo, SolidWorks API contracts, and prototype. Critically evaluate the algorithm **before** changing code. Then implement a production-quality, isolated **Circle Along Face** command inside the existing TAI TOOL add-in. Work on the separate branch; do not merge to main or change unrelated commands. Report your decisions and evidence, not merely that code was written.

### Exact user intent — read carefully
- User chooses **one surface FACE** on a twisted/nonplanar sheet-metal Part in SolidWorks (not an edge, not two sketches). App should infer the two curved opposite end boundaries: **small end** and **large end**, from topological/geometric analysis; reject/ask for disambiguation when not confident.
- Place **N straight round tubes**, default **N=41** and outside diameter **20 mm**, along that sheet surface, forming a fan across the twisted face. At the **small end**, neighboring tubes should nearly touch in actual 3D (zero nominal gap, controllable tolerance). At the **large end**, tubes spread evenly over the true measured curved boundary with symmetric end margins (default 5 mm measured from tube outside to end edge). Tube count 40 + option "Add +1" => 41.
- Tube centerlines cannot simply be 2D circular pattern instances, nor can 20 mm arc-length spacing be assumed to guarantee cylindrical contact once rods tilt in 3D.
- User MUST be able to **Flip Side**, reversing which side of the face the tubes lie on. All tubes must have coherent orientation and stay on the selected side.
- User wants **3D Sketch AXES ONLY** initially, not actual Solid cylinders. Preview before creating *any* persistent geometry. Cancel means no added feature/geometry.
- Debug output **every run**, even failure; users previously suffered short SolidWorks message windows.

### Existing project and isolation
- Legacy .csproj, .NET Framework 4.8, x64, C# 7.3, SolidWorks Interop.
- Existing `SWAddin.cs` uses `ISwAddin` and TaskPane `BomTaskPaneControl`.
- Avoid touching behavior of Check Same Part, Check Drawing BOM, Check Hole, DXF, V-Cut, or other classes.
- Keep all new geometry code under a dedicated namespace/folder, e.g. `Commands/CircleFaceTubeLayout*.cs` + `Geometry/CircleFaceTube*`.
- Only minimal wiring in `ADDIN.csproj`, a new button in TaskPane, and optional lazy-open UI. No startup expensive topology scans. Avoid modifying existing registration/addin GUID, existing callbacks, global command state.
- Consider COM ownership, SolidWorks STA/UI-thread calls, events/lifetimes, rollback, exception handling, cancellation and prior heap corruption incidents. Do not blanket-release COM objects held by SW.

## Mandatory analysis before implementation
1. Read prototype line by line. Prove which API signatures return the claimed types for `Face2.GetEdges`, `GetLoops`, `Edge.GetCurveParams3`, `Curve.Evaluate2`, `Curve.GetLength3`, `Surface.EvaluateAtPoint`, `Face2.FaceInSurfaceSense`, `SketchManager.CreateCenterLine`, `Insert3DSketch`, `Sketch.GetFeature`. Verify normal-vector ordering and orientation: the prototype may be WRONG. Repair it, do not cargo-cult.
2. Face boundary extraction: identify exterior loop and connected edge chains, handle orientation using coedges. Detect two "end rails" even if split into multiple edges; detect ambiguous cases and fail safely. Do not infer solely from edge length or "bow" if it can be wrong.
3. Geometry: measure true end-rail arc length, evaluate points at equal arc-length parameters using validated `ICurve` APIs (not equal curve-parameter steps). Align corresponding directions of small and large rails; verify endpoint pairing in 3D.
4. **Physical constraint issue:** A straight finite cylinder generally cannot remain exactly tangent along an arbitrary twisted surface. Define the user-approved hierarchy: (a) no tube/sheet penetration, (b) no tube/tube penetration, (c) small-end nominal contact, (d) large-end uniform distribution/end symmetry, (e) minimum deviation from selected face and straight axes. If constraints conflict, return `INFEASIBLE` with details instead of pretending success.
5. The initial axes from independently offset boundary points by local normals are only seeds. Optimize 3D positions/orientations and do precise collision verification (or explicitly report preview approximate/unverified). Include cylinder end caps, finite axis distances, near-tangent tolerance, face curvature and twist.

## Expected UX
New `Circle Along Face` button. Workflow:
- Select one Part face -> open settings. Defaults: OD 20 mm, 40 base count, Add +1 ON, total 41, large end margin 5 mm, Flip Side OFF.
- **Analyze**: show selected face, recognized end rails with two different colors, calculated small/large arc lengths, N, small-end pitch/contact deviations, large-end pitch/margins, tube/face collision summary.
- **Preview**: ideally lightweight temporary 3D lines (and optionally transparent cylindrical envelopes) using supported transient graphics, not features. User must be able to flip side, add +1, and change settings while preview updates.
- Three actions: **Preview / Create 3D Sketch / Cancel**, plus **Open Debug Log**. The Create action must be blocked by unrecoverable geometry errors; allow clearly labeled approximate-only outcome solely when the user explicitly accepts it.
- Create **one new named 3D Sketch with N construction centerlines**, preserve original face/body, restore selection/SketchManager state, Undo works, avoid partial feature on error.
- UI should be visual (small-end touching vs large-end spaced), not solely a text wall. No invasive edit to existing TaskPane tabs.
- Launch only on demand; no blocking on addin startup.

## Debug — required
Write incremental UTF-8 log per execution:
`%LOCALAPPDATA%\TAI_TOOL\CircleFaceTubeLayout\TUBE_FACE_yyyyMMdd_HHmmss_fff.log`.
Include document path, configuration, add-in version/commit, selected face identity, surface type, loop/coedge details, boundary point coordinates, chosen small/large rail grouping, arc lengths and orientation, normal orientation/Flip, numerical solver settings and convergence, each tube axis endpoints (in **meters**, labeled), distances in **mm**, small-end contact error, nearest tube pair separation, face-penetration estimates, failures/exception stack trace, preview vs commit outcomes. Keep logs even if SolidWorks exits unexpectedly. Add concise on-screen messages.

## Sample design / test numbers (reference, NOT hardcode)
From conversation examples:
- **Small end:** R = 3247 mm; arc length = 811.35 mm.
- **Large end:** R = 4323.16 mm; arc length = 1421.97 mm.
- N = 41; OD = 20 mm; large-end outer-edge clearance = 5 mm each side.
- For pure planar small-circle geometry the 20 mm **chord** contact pitch corresponds to arc pitch `2*R*asin(20/(2*R)) ≈ 20.00003162 mm`; 40 steps take about 800.001265 mm, so symmetric first-center margin ≈ **5.674368 mm** and tube edge overhang ≈ **4.325632 mm** each side (permitted).
- Large-end center-to-center along boundary: `(1421.97 - 2*(10+5))/40 = 34.79925 mm`.
- These are mathematical checks only; do not misrepresent them as exact 3D contact/clearance for twisted geometry.
- An actual user Part sample named **`4313-04-HU-H1.SLDPRT`** was supplied in conversation (do **not** commit proprietary CAD file into public repo). Run manual SolidWorks checks with user's local copy when available.

## Required test matrix and deliverables
Tests (pure geometry where possible) for flat annular sector, known R1/R2, twisted face, different edge orientations, flipped face, split edges, spline edges, missing/invalid face, holes/loops, degenerate curves, N=40 vs 41, too many tubes, small-end overhang, face penetration, tube-tube collision, rollback/cancel and repeated runs. Prefer tests that run without SOLIDWORKS for math layer, manual SolidWorks tests for COM/API/UI layer.

**Provide before coding:**
- A succinct design review identifying every risky assumption in prototype and chosen feasibility/optimization method; short plan.

**Provide after coding:**
- Exact changed-file list and diff rationale, x64 Debug/Release build logs (or clear reason unavailable), geometry table for N=41, screenshot/screencast of real model preview if possible, path to debug log, test failures and unresolved limitations, CI/test command, branch and PR links.
- Build/run in SolidWorks 2024 SP5.0 + Windows 11 when possible. If not available, do not claim compilation or runtime success.
- Never silently change behavior of unrelated commands. No auto-merge to `main`. Put work on this branch and open a **draft PR** for review.

**First response to the user should evaluate this design**, state any corrections and then proceed with code and tests. Avoid small piecemeal patches: consolidate all identified issues into one coherent implementation plan.
