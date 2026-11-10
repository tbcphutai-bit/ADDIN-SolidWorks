# Circle Along Face

The integrated command is available under **Model → Macro → Circle Along Face**. Select one face of the sheet in a Part, or one face of a resolved top-level Part component in an Assembly. In an Assembly the resulting sketch belongs to the Assembly. The settings start at Ø20 mm, base count 40, **Add +1** on, and 5 mm large-end clearance.

**Preview** shows temporary axes and two end rails on the model. Green means the geometry checks passed. **Flip Side** and **Add +1** recalculate the preview. **Create 3D Sketch** creates one named sketch containing construction lines. **Cancel** clears the temporary graphics. The success dialog offers **Undo Sketch** to remove the new feature directly. SOLIDWORKS may decline a grouped Ctrl+Z entry for an Assembly sketch. The command writes an incremental UTF-8 log under `%LOCALAPPDATA%\TAI_TOOL\CircleFaceTubeLayout`.

The exploratory files in this directory and `CODEX_PROMPT.md` describe the original prototype. The compiled command is in `Commands/CircleFaceTubeLayout`.

## Geometry decisions

- The exterior coedge loop determines connectivity and orientation. Smooth splits are combined into chains and four discernible corners are required. Opposite-chain shape and taper determine the end pair, subject to a 3D pairing check. Ambiguous boundaries are rejected.
- Arc lengths are measured on SolidWorks curves. A managed station cache is used for the iteration, followed by exact curve evaluation of the chosen stations. The large stations are evenly spaced in arc length. The edge clearance is measured from the outer support of each terminal cap to the plane normal to the inward rail tangent at the corresponding corner.
- Small-end contact is measured on the first OD-length section of each finite 3D cylinder, using convex support for the barrel and flat caps. A 0.005 mm nominal solver gap allows numerical headroom. The contact band is 0–0.05 mm and positive separation is required over the full length for every pair.
- SolidWorks B-rep intersection checks use temporary cylinder envelopes and the selected trimmed sheet face or the owning solid sheet body. A sampled signed clearance guides a bounded shared normal lift. Tube positions are re-solved and rechecked after a lift. The lift and the sampled clearance are reported separately from the kernel intersection result.
- A straight tube can bridge a twisted face. This search allows at most two diameters of common lift and does not prove that no solution exists outside its search family. A component's other Assembly neighbors are outside the collision scope. Assembly-level geometry overrides, subassembly components and unresolved components require the underlying Part or owning subassembly to be opened directly.

## Verification

Use the Visual Studio Developer PowerShell from the repository root to run the pure geometry regression suite.

```powershell
New-Item -ItemType Directory -Force bin/CircleFace | Out-Null
csc /nologo /langversion:7.3 /platform:x64 /out:bin/CircleFace/GeometryTests.exe Tests/CircleFaceTubeLayout/GeometryTests.cs Commands/CircleFaceTubeLayout/TubeGeometry.cs Commands/CircleFaceTubeLayout/TubeLayoutSolver.cs Commands/CircleFaceTubeLayout/BoundaryRails.cs
& .\bin\CircleFace\GeometryTests.exe
```

Build `ADDIN.csproj` with x64 Debug and Release against the SolidWorks 2024 Interop DLLs. To build without installing, pass `/p:PostBuildEvent= /p:RegisterForComInterop=false` and choose an output directory inside the checkout.

Manual SolidWorks 2024 checks on a disposable copy:

1. Select the broad sheet face. Check that the preview rails match the small and large ends from a view where both are visible.
2. Toggle **Flip Side** and **Add +1**. Confirm side, 40/41 count, pitch, and cap margins in the report. Hide existing tube components if they obscure the preview.
3. Cancel once and check the feature tree. Run again, create the 3D sketch, verify construction geometry, then use Undo Sketch in the success dialog.
4. Try no selection, multiple faces, a split or spline rail, a sheet with an inner loop, an ambiguous boundary and a count that does not fit. Inspect the debug log for the reason and check that a rejected preview cannot create a sketch.
