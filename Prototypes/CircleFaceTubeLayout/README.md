# CircleFaceTubeLayout — isolated prototype

**Status:** Prototype source and Codex task are pushed. **Not integrated, not built, not run in SolidWorks.** This directory is intentionally OUTSIDE the existing legacy .csproj Compile list. Existing commands and runtime behavior remain unchanged.

## Files
- `Commands/CircleFaceTubeLayoutCommand.cs`: initial face-selection, four-edge rail analysis, arc-length spacing, provisional 3D centerline placement, rough clash precheck, named 3D Sketch creation after Yes, incremental debug.
- `Commands/CircleFaceTubeLayoutForm.cs`: experimental settings for OD, count, Add +1, Flip, large-end margin.
- `CODEX_PROMPT.md`: full implementation requirements and acceptance tests for Codex.

## Why isolated?
The user's twisted face requires 3D cylinder placement and verification beyond a simple circular pattern. Initial math is approximate and COM signatures have NOT been validated against a local SolidWorks 2024 installation. Merging this directly into the add-in could cause compile failures or unsafe modeling.

## User workflow target
Select ONE Part face -> infer short and long curved boundaries -> arrange 41 straight φ20 tubes touching at small end and evenly spaced at large end -> Flip if necessary -> transient graphic preview and debug -> explicit confirmation -> create one 3D Sketch of 41 construction axes. No Solid bodies until the user chooses that later.

## Reference numerical test
- Small: R3247 mm, length 811.35 mm
- Large: R4323.16 mm, length 1421.97 mm
- N41, OD20, big-end edge margin 5 mm: big-end pitch 34.79925 mm
- Small-end approximate arc pitch for a 20 mm chord is 20.00003162 mm; **this is not proof of tube/tube contact on a twisted surface**.

## Geometry and safety limitations
- Only exactly four face edges and one outer loop.
- Assumes two opposite bowed edges; can misdetect on other faces.
- Provisional straight axes are calculated by interpolated endpoint normals, not optimized for true tangent contact.
- Reported collisions are sampled/axis-distance estimates, not SolidWorks interference certification.
- Only numeric `MessageBox` preview; no transient 3D graphics.
- Sketch creation can leave a partial sketch after exceptions: Codex must implement rollback.
- Debug target `%LOCALAPPDATA%\TAI_TOOL\CircleFaceTubeLayout`.
- No uploaded proprietary `.SLDPRT` file is committed here.

**Next action:** Give Codex this branch and ask it to execute `CODEX_PROMPT.md`. Integrate only after architecture review, API checks, compile tests, and real CAD validation. Open a draft PR and do not merge to main without user approval.
