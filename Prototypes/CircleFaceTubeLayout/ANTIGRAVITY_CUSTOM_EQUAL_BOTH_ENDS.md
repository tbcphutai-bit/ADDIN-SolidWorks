# ANTIGRAVITY IMPLEMENTATION HANDOFF — Circle Along Face, custom both-end spacing
Date: 2026-11-17
Repository: https://github.com/tbcphutai-bit/ADDIN-SolidWorks
Working branch: feature/circle-face-custom-equal-both-ends-20261117
Draft PR: https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/64
Parent draft PR: https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/63

## 0. Mandatory workflow and safety of the existing Add-in

- **Think first**: inspect all related sources and report any actual design flaw before changing code. Do not rewrite the entire feature if an existing implementation satisfies the requirement.
- Start from working branch above, not main. Do not merge either PR.
- **Only change** \`Commands/CircleFaceTubeLayout/*\`, \`Tests/CircleFaceTubeLayout/*\` and closely related dedicated documentation. Avoid modifying CheckDrawingBom, CheckSamePart, CheckHole, unrelated classes, or existing command callbacks. If wiring is already present, do not touch Task Pane or ADDIN.csproj.
- Keep C# compatible with SolidWorks 2024 COM and .NET Framework 4.8 / C# 7.3 x64.
- Work in a duplicate of any customer Part before creating geometry. Do not place, save, or modify customer binary models in the repository.
- Source changes on PR #64 are NOT yet verified by a SolidWorks build or runtime test. Your job is to fix, build, test, debug, document — not claim success before evidence.

## 1. Precise desired behavior

From **exactly one selected sheet face** (potentially curved and twisted), automatically identify the two opposite short/long boundary rails and generate a preview of N 3D tube axes. Each tube has default **outside diameter φ20 mm** and runs from a corresponding station on the short end to a corresponding station on the long end.

The operator chooses:

- Auto count: old touching-small-end behavior is unchanged.
- Custom count N: two optional submodes:
  - \`TangentContact\`: small-end tubes near-touch; long-end stations evenly distributed (OLD).
  - \`EqualPitch\`: both short and long ends are **independently** divided into N equally spaced arc-length stations; actual 3D end-cap margins at both sides are balanced (NEW). Small-end tubes are *not required to touch* in this mode.
- Existing Add +1, Flip Side, Preview, Cancel, Create 3D Sketch, Open Debug Log continue to work.

**No tube solids yet.** Explicit Create generates only 3D construction axes. Cancel and failed validation must not change the model.

### Essential interpretation

"Chia đều hai đầu" is not a constant gap everywhere along the 3D tube length and is NOT a single circle-pattern rotation. It means N stations of uniform **arc-length** pitch on EACH boundary, potentially with different pitches and 3D tilt. In EqualPitch mode, nonzero small-end gap is expected and is not an error. Body interference is still an error.

## 2. Authoritative files and current implementation

Read these complete files **before editing**:

1. \`Commands/CircleFaceTubeLayout/TubeLayoutSolver.cs\`: \`SmallEndSpacingMode\`, \`Solve\`, \`SolveCore\`, \`SolveEqualStations\`, \`SolveStations\`, \`VerifyExact\`, \`VerifyTubes\`.
2. \`Commands/CircleFaceTubeLayout/SwFaceLayout.cs\`: face/rail acquisition, normals, \`Analyze\`, sheet clearance and BREP checks.
3. \`Commands/CircleFaceTubeLayout/CircleFaceTubeLayoutForm.cs\`: controls, invalidating preview, button states, modeless-ish preview timer and dialog lifecycle.
4. \`Commands/CircleFaceTubeLayout/CircleFaceTubeLayoutCommand.cs\`: face selection, preview, COM graphics cleanup, create/undo 3D sketch, log path.
5. \`Commands/CircleFaceTubeLayout/TubeGeometry.cs\`, \`BoundaryRails.cs\`.
6. \`Tests/CircleFaceTubeLayout/GeometryTests.cs\`.

The code already implements the new mode. **Use the code as your starting point**; fix it if necessary. Do not duplicate the same solver in a second class.

## 3. Core code contract (preserve/refine this logic)

The current enum and Solve signature must remain source compatible:

\`\`\`csharp
internal enum SmallEndSpacingMode
{
    TangentContact,
    EqualPitch
}

public static TubeLayout Solve(
    Func<double, RailStation> small, double smallLength,
    Func<double, RailStation> large, double largeLength,
    int count, double diameter, double margin, double lift,
    Action<string> log, bool verify = true,
    SmallEndSpacingMode smallMode = SmallEndSpacingMode.TangentContact)
\`\`\`

In \`SwFaceLayout.Analyze(...)\`, when Custom N is used, pass \`smallMode\` into \`Solve\`. When Auto N is used, retain \`SolveAutomatic\` and \`TangentContact\`. The UI passes the option through its \`analyze\` delegate into the command and face analyzer.

Reference formula for nominal station placement (everything is **meters** inside SolidWorks API):

\`\`\`csharp
double firstShort = diameter * 0.5 + margin;
double lastShort  = diameter * 0.5 + margin;
double shortPitch = (shortLength - firstShort - lastShort) / (count - 1);

double firstLong = diameter * 0.5 + margin;
double lastLong  = diameter * 0.5 + margin;
double longPitch = (longLength - firstLong - lastLong) / (count - 1);

for (int i = 0; i < count; i++)
{
    double shortS = firstShort + i * shortPitch;
    double longS  = firstLong  + i * longPitch;

    RailStation sa = shortRail(shortS);
    RailStation sb = longRail(longS);

    // RailStation.Normal is the local selected face normal,
    // with Flip Side sign already applied.
    Point3 a = sa.Point + sa.Normal * (diameter * 0.5 + lift);
    Point3 b = sb.Point + sb.Normal * (diameter * 0.5 + lift);
    TubeAxis axis = new TubeAxis(a, b);

    // Store these endpoints and station distances for preview/debug/3D sketch.
}
\`\`\`

**These formulas are only the nominal starting approximation.** When axes are tilted, the end-cap radial projection onto each edge direction changes. The existing \`CapClearance\` and iterative \`SolveEqualStations\` / \`SolveCore\` must correct first/last station positions independently to make actual **projected cap clearance** as close to the requested margin as permitted by the current tolerance. Re-evaluate on exact SolidWorks geometry in \`VerifyExact\` and reject any unsuccessful correction. Do not claim "5 mm" from nominal arc-center distances.

Preserve these safety guards:

\`\`\`csharp
if (count < 2 || diameter <= 0 || margin < 0 || lift < 0)
    throw new LayoutException("Invalid tube layout settings.");

if (shortLength <= diameter + 2 * margin ||
    longLength <= diameter + 2 * margin)
    throw new LayoutException("INFEASIBLE: insufficient arc length for requested cap margins.");

// Do not check contact tolerance in EqualPitch:
if (layout.SmallMode == SmallEndSpacingMode.TangentContact &&
    contact.Upper > ContactTolerance)
    layout.Issue = "Small-end contact tolerance exceeded.";

// ALWAYS check full 3D finite-cylinder collisions in both modes.
// "Uncertain" distance/solid interference is NOT "verified clear".
\`\`\`

If an early guard is only a conservative pre-check, document that clearly; do not turn nominal pitch < diameter alone into a false 3D certification or false collision result. The existing solver uses GJK distance bounds for finite cylinders. Verify its lower/upper-bound interpretation under tests. Reject unproven cases safely.

### UI code contract

Retain:

\`\`\`csharp
private readonly ComboBox smallMode =
    new ComboBox { Width = 177, DropDownStyle = ComboBoxStyle.DropDownList };

private SmallEndSpacingMode SmallMode =>
    smallMode.SelectedIndex == 1
        ? SmallEndSpacingMode.EqualPitch
        : SmallEndSpacingMode.TangentContact;
\`\`\`

On form initialization, add:
\`\`\`csharp
smallMode.Items.AddRange(new object[]
{
    "Touch at small end (existing)",
    "Equal pitch at both ends"
});
smallMode.SelectedIndex = 0;
\`\`\`

Enable the selector **only** when Auto count is unchecked. If the user changes count, diameter, margin, Add +1, Flip Side, spacing mode, or Auto/Custom toggle, cancel previous acceptance, clear old preview, rerun validation and keep Create disabled until verified. In Auto, force the effective solver mode to TangentContact regardless of the selector's previous state. Ensure no Timer reentrancy or use-after-dispose.

### Mandatory reporting

Preview and debug must include:
- selected Face identity, Part/configuration, short/long rail lengths;
- count N, base count and Add +1, D, margin, Flip Side, chosen spacing mode;
- short/long stations \`s[i]\`, short/long pitches;
- true 3D left/right cap margins on BOTH ends in EqualPitch mode;
- each axis 3D endpoints, tube length;
- minimum adjacent small-end gap (nonzero OK in EqualPitch);
- minimum full-length pairwise clearance and any colliding/uncertain pair;
- tube-to-sheet clearance result and SolidWorks BREP check;
- failure reason, even if Create remains disabled.

Log directory already used by code:
\`%LOCALAPPDATA%\TAI_TOOL\CircleFaceTubeLayout\`.

## 4. Regression fixture, expected nominal results

Assume test input measured directly from model (NOT fixed in production code):
- short rail = 811.35 mm, long rail = 1421.97 mm;
- outside diameter = 20.0 mm;
- requested cap margin each side = 5.0 mm.

For equal-pitch custom mode:
- N=20 -> short pitch = 781.35 / 19 = 41.123684 mm; long pitch = 1391.97 / 19 = 73.261579 mm.
- N=30 -> short pitch = 781.35 / 29 = 26.943103 mm; long pitch = 1391.97 / 29 = 47.998966 mm.
- N=40 -> short pitch = 781.35 / 39 = 20.034615 mm; long pitch = 1391.97 / 39 = 35.691538 mm.
- N=41 -> short pitch = 781.35 / 40 = 19.53375 mm (less than 20 mm diameter), so the test model can be infeasible with non-overhanging 5 mm margins. Confirm by finite-cylinder 3D collision checks, not just this nominal spacing comparison.

These are **nominal arc stations**; finite tube cap-projection corrections and twist may alter actual computed pitch slightly.

Add/retain tests for:
- Old contact mode 40/41 and Auto count unchanged.
- Custom EqualPitch 20/30/40: all stations increasing, equal pitch, balanced actual margins, full pair clearance.
- Custom EqualPitch N=41: explicit INFEASIBLE if 3D interference is found; Create disabled.
- Flat and twisted rails, Flip Side both directions, varied margins and tube diameters.
- 2D arc pitch versus 3D chord, finite cylinder collision, crossing and near-touch.
- Preview Cancel and failure leaves no Sketch, Preview alone is non-destructive.
- Original Add +1 still adds exactly one, no double count, N never exceeds MaximumCount.
- Face with multiple coedge pieces per rail, reverse coedge orientation, ambiguous/non-four-sided topology.
- SolidWorks 2024 on a real copy of \`4313-04-HU-H1.SLDPRT\` if available locally. Otherwise clearly state this runtime test was not performed.

## 5. Work sequence for Anti

1. Checkout this feature branch. Read PR #63 and #64 diffs and all listed sources.
2. Produce a short risk report first: exact functions, bugs/pitfalls, planned changes.
3. Compile x64 .NET Framework 4.8 using SolidWorks 2024 interop on Windows; list compiler failures with file+line and correct them. Do not guess API signatures.
4. Run standalone geometry regression tests. Investigate test assumptions if a case fails; DO NOT fake/pass or bypass geometry validation.
5. Test UI state changes and preview logic; ensure no modifications without explicit Create.
6. Test real sheet/twisted part with a copy and save debug logs. Report which tests were not possible.
7. Make one consolidated set of fixes based on the whole analysis; avoid incremental random edits.
8. Commit only related changes on the branch, update PR #64. Do NOT merge.
9. Produce \`Prototypes/CircleFaceTubeLayout/ANTI_IMPLEMENTATION_REPORT.md\`: changed paths, commit hash, build commands/results, actual executed test outcomes, geometry logic, collision findings, log paths, unresolved issues, next actions for Codex/ChatGPT. If a requirement cannot be met without violating 3D geometry, explain the exact constraint and alternatives. Do not silently change count or geometry.

## 6. Acceptance criteria

- One face selected; short and long curves identified correctly.
- Auto keeps old contact mode behavior.
- Custom allows existing touching mode and new equal-pitch-both-ends mode.
- N matches UI exactly, including Add +1.
- Both ends independently equal pitch by actual curve arc length; symmetric end-cap clearances as close as tolerance allows.
- Preview before commit; Flip Side works in 3D; no accidental Sketch on Cancel.
- Collision / sheet interference checks respected; uncertain = blocked.
- Debug log exported per run, with status and coordinates.
- No changes to unrelated Add-in commands.
- PR #64 is a DRAFT until SolidWorks 2024 build and real 3D runtime validation are reported.

## 7. Handoff to Codex after Anti

Codex should review Anti's \`ANTI_IMPLEMENTATION_REPORT.md\`, commits and debug logs, audit collision/normal math independently, propose the final corrective approach if needed and only then approve merging.