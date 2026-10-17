# Company DXF profiles — Phase 2 through Phase 3C

Production code is in `Commands/BendToolProfiles/`. It has no SOLIDWORKS, WinForms,
or external package dependency. It supplies read-only geometry/provenance to the
existing `BendCheckSession`; it does not evaluate manufacturing feasibility.

Phase 3A adds independent analytic region validation and optional private datum
evidence. Source closure and manufacturing status retain their Phase 2 meaning.
See [geometry/evidence contract](../../Docs/BendCheck/Phase3A-GeometryContract.md)
and [validation results](../../Docs/BendCheck/Validation-Phase3A.md). No UI change
or Check Max feasibility algorithm is introduced.

Phase 3B adds `Commands/BendPlacement/`: private Master punch/holder selections,
rigid placement math, whole sheet coverage and independent workbook reference
mapping. The native adapter appends analytic arc metadata during the existing
STA capture; workers still receive only managed file/geometry data. See the
[placement contract](../../Docs/BendCheck/Phase3B-PlacementContract.md) and
[Phase 3B validation](../../Docs/BendCheck/Validation-Phase3B.md).

Phase 3B.1 adds a **separate** bounded SPLINE holder/material-set backend,
explicit component/hole/union roles, exact reviewed mapping and signed external
seating records. It does not change the legacy analytic DIM/punch flags, Min,
UI or command flow. See the [holder/evidence contract](../../Docs/BendCheck/Phase3B1-HolderContract.md)
and [18-tool validation](../../Docs/BendCheck/Validation-Phase3B1.md).

Local-only Phase 3B.1 QA (outputs include private CAD and must stay outside Git):

```powershell
& $ToolProfilesTestExe --holder-company $PlacementConfig $CompanyDimDirectory $PrivateOutput
python Tests/BendToolProfiles/compare_holder_splines.py `
  "$PrivateOutput/PRIVATE-spline-qa.json" "$PrivateOutput/PRIVATE-spline-independent.json"
```

The independent script requires ezdxf only for QA. It evaluates original WCS
controls/rational curves and nested INSERT transforms against each certified
chord band. The local integration also emits **UNFILLED** material/mapping,
seating and authority templates plus a machine operator checklist; it supplies
no approval, measured pose, signature or key and does not configure the templates.
Actual source tests assert 18 slots, 17 valid punches, no physical verification,
120 converted selected SPLINE instances and four unresolved associations.

Phase 3C adds the executable managed kernel in `Commands/BendCollision/` and
adapters to the existing placement candidates. It computes conservative
Workpiece/Punch and Workpiece/Holder geometry results, independent of the
unchanged manufacturing verdict. No collision package or QA package is loaded
by the add-in. See the [collision contract](../../Docs/BendCheck/Phase3C-CollisionContract.md)
and [observed validation](../../Docs/BendCheck/Validation-Phase3C.md).

The normal executable runs all pure C# tests, including real CLEAR/COLLISION/
UNKNOWN fixtures, 54/108-row integration and an all-18 synthetic externally
reviewed setup fixture with no selected tool. Those generated reviews and
triangle/rectangle tools are explicitly software test data, never company
approvals or substitute company profiles.

```powershell
& $ToolProfilesTestExe
& $ToolProfilesTestExe --collision-company $PlacementConfig $CompanyDimDirectory $PrivateOutput
# Independent oracle consumes only controlled synthetic geometry:
& $ToolProfilesTestExe --collision-oracle $PrivateOracleOutput
python Tests/BendToolProfiles/compare_collision_with_geos.py $PrivateOracleOutput
```

Optional oracle dependencies are Shapely/GEOS (tested with Shapely 2.2.0) and
NumPy in a private QA environment. The oracle independently subtracts each
component's holes before union and uses a declared `1e-7 mm` arc sagitta
allowance. It checks clearance lower bounds, conservative SPLINE bands and
robust interior witnesses. Tessellation is restricted to QA. Production LINE/
ARC distances and predicates use outward interval enclosures. Actual company
sources remain UNKNOWN until the separate source/setup evidence is provided.

Native validation also asserts all 18 collision rows, explicit missing states,
stale held results, unchanged Min and one model capture. Source-error snapshot
comparisons retain private before/after diagnostics if a native assertion fails.

Local-only Phase 3B modes (all output must be outside the checkout):

```powershell
& $ToolProfilesTestExe --workbook-objects $Workbook $PrivateObjectInventory
& $ToolProfilesTestExe --placement-company $PlacementConfig $CompanyDimDirectory $PrivateOutput
& $NativeToolLibraryTestExe $ProductDll $PrivateOutput $OriginalPart $CompanyDimDirectory $MinExcel $PlacementConfig
```

The company integration uses actual supplied tools; its sheet is an explicitly
synthetic harness fixture. Native validation uses a private copy of the real
Part. Debug diagnostic injection is asserted in Debug; Release omits
Debug.WriteLine and asserts it does not run. Production source code never edits
the Part. Native stale/configuration tests edit only the harness's private copy.

## Source configuration without UI changes

Create a private companion config at
`%LOCALAPPDATA%/ADDIN/bend-tool-dxf-source.json`:

```json
{
  "Version": 1,
  "DirectoryPath": "C:\\Company\\BendTools\\DXF",
  "ManifestPath": null
}
```

This leaves the Excel preference schema independent. Alternatively inject
`ToolProfileLibraryProvider(Func<ToolProfileSourceConfiguration>)`, or set process
environment variables `ADDIN_BEND_TOOL_DXF_DIRECTORY` and optionally
`ADDIN_BEND_TOOL_DXF_MANIFEST`. The environment directory takes precedence over
the companion config. The add-in never creates/exports CAD or settings beside
the company data. A missing/unconfigured source produces 18 unverified records.

The default layout is **CompanyDimensionedV1**: each of the exact 18 IDs in
`CompanyToolIds.All` resolves to its lowercase `id_DIM.dxf` filename. Exactly one
model-space LWPOLYLINE/POLYLINE must exist. It is a source selection convention,
not proof of physical tool identity. An ambiguous file is not reduced to its
first/largest outline. Other lines remain `UnclassifiedReference`; text,
dimensions and paper-space content remain `Annotations`.

For other source layouts, set `ManifestPath` to a private JSON file (absolute,
or relative to `DirectoryPath`). Example using **synthetic names**:

```json
{
  "Version": 1,
  "Tools": [
    {
      "Id": "AMD-STD-003",
      "Sources": [
        { "File": "example.dxf", "Role": "CompanyPunchProfile", "Handles": ["AB"] },
        { "File": "reference.dxf", "Role": "HolderClampReference",
          "Blocks": ["REFERENCE"], "InsertHandles": ["CD"] }
      ]
    }
  ]
}
```

Selectors `Handles`, `Layers` (effective layer), `Blocks`, `InsertHandles` and
`EntityTypes` combine with AND across fields / OR within a field. Insert handles
distinguish repeated instances of the same block. `ExpectedSha256` can pin a
selection to exact source bytes. `SinglePolyline` can require a unique polyline.
Sources must stay inside the configured directory. Missing IDs remain explicit
missing entries; an invalid manifest does not fall back to guessed selections.

Roles: `CompanyPunchProfile`, `HolderClampReference`, `ManufacturerReference`,
`Annotations`, `UnclassifiedReference`; `VerifiedHolderGeometry` is reserved.
A request for that last role is retained as reference with an explanation.
These legacy DIM-profile flags (`ClampVerified`, `ToolTipOriginVerified`,
`CoordinateFrameVerified`) remain false. The distinct Phase 3B.1 Master-pair
contract requires authorized external evidence and does not promote a raw role.
Text and
dimensions cannot be promoted to punch geometry by a selector. Overlapping
selectors are downgraded to unclassified geometry and reported as incomplete.

## Geometry and freshness contract

The geometry list below describes the legacy analytic DIM/punch reader. The
separate Phase 3B.1 holder backend preserves those analytic entities and adds
bounded SPLINE material geometry under the explicit contract linked above.

- LINE, ARC, CIRCLE, LWPOLYLINE bulges, 2D POLYLINE/VERTEX/SEQEND, nested INSERT.
- Exact analytic lines/circles; no tessellation, invented closure or tip shift.
- Entity source handle/record, original/effective layers, block/instance path,
  INSERT handles, original polyline vertices/bulges/closed flag, final transform.
- Curve coordinates/radii are in mm. Supported INSUNITS: inch, foot, mm, cm, m,
  mil, yard, micron and decimetre. Missing/unitless/other units are unverified;
  the reader does not consult registry defaults or guess from dimensions.
- Header INSBASE remains raw source metadata; it is not treated as a verified
  punch-tip origin. BLOCK base point and INSERT OCS/scale/rotation are applied.
- XY geometry with normals parallel to +/-Z is supported, including mirrored
  arc orientation. Nonzero Z/elevation/thickness, tilted OCS, meshes/fitted or
  width polylines, unresolved xrefs and SPLINE are explicitly unsupported.
- Uniform/reflected circular transforms preserve R; a nonuniform/sheared arc
  would be an ellipse and is reported unsupported. Lines retain valid affine
  transforms. Closure observes endpoints/source flags, not area validity,
  self-intersection or collision regions.
- SHA256 is computed from saved bytes on each background refresh (about two
  seconds while the existing session polls). Unchanged hashes reuse the parsed
  document. Changed/missing files or manifest invalidate the old snapshot;
  errors do not retain last-good geometry as current. Changed length/mtime during
  a read is rejected. No filesystem read or parsing runs in the UI poll itself.
- `PunchGeometryReadComplete` and `PunchEntitiesSupported` describe parsing only.
  Snapshot/validation status is always `CHUA_XAC_MINH`. Check Min, bend inventory
  and candidate-order evaluation are independent.

`[BEND TOOL LIBRARY]` is a separate summary of IDs, hashes, roles, counts and
unverified reasons; it excludes curve coordinates. The full geometry snapshot
is available on the session/item for a later verified Check Max implementation.

Reference rules: Autodesk [ARC](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-0B14D8F1-0EBA-44BF-9108-57D8CE614BC8.htm),
[LWPOLYLINE](https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm),
[POLYLINE](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-ABF6B778-BE20-4B49-9B58-A94E64CEFFF3.htm),
[INSERT](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-28FA4CFB-9D5E-4880-9F11-36C97578252F.htm),
[BLOCK](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-66D32572-005A-4E23-8B8B-8726E8C14302.htm),
[OCS](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-D99F1509-E4E4-47A3-8691-92EA07DC88F5.htm).

## Pure C# tests

```powershell
& $MSBuild Tests/BendToolProfiles/ToolProfiles.Tests.csproj /t:Rebuild /p:UseSharedCompilation=false
& Tests/BendToolProfiles/bin/ToolProfiles.Tests.exe
```

No PIAs/CAD required. All geometry is synthetic test data, never a substitute
company tool. Tests exercise signed/major bulges, open/closed flags, R, units,
OCS, INSERT/base/mirror/nesting/arrays, unsupported/malformed input, role isolation,
missing source, hash/manifest invalidation and asynchronous failure isolation.

## Private company integration and independent QA

Always keep `$PrivateOutput` outside the repository. The commands below read CAD
in place; reports contain geometry and must not be uploaded.

```powershell
& $ToolProfilesTestExe --company $CompanyDimDirectory $PrivateOutput $IndexDxf $MasterDxf
# Or: --company $CompanyRoot $PrivateOutput --manifest $PrivateManifest
```

Local integration asserts 18 readable profiles / 17 closed / KIK open, source
hashes unchanged, and unverified tip/frame/clamp. It can also retain optional
aggregate DXF evidence. `compare_with_ezdxf.py $PrivateOutput` independently
compares supported curves against original files using `ezdxf` (QA dependency
only; tested with 1.4.5), including arc radius and arc midpoint after OCS/INSERT.
The two aggregate evidence files must be present for that comparison.

## Native regression

Build product Debug/x64 into a private directory with the existing pre/post-build
events disabled, as in `Tests/BendPlanning/README.md`. Then:

```powershell
& $MSBuild Tests/BendToolProfiles/NativeToolLibrary.Validation.csproj /t:Rebuild
& $NativeToolLibraryTestExe $ProductDll $PrivateOutput $PrivatePart $CompanyDimDirectory $ExcelPath
```

SOLIDWORKS 2024 / x64 PIAs are needed only for native validation. The harness
opens the existing production Check chấn form in a newly owned API process,
using a private Part copy. It verifies source failure/recovery/diagnostic failure,
no second COM capture on source changes, unchanged Min/planning, and existing
rebuild invalidation. It does not replace/register the installed DLL or alter
machine/user environment settings. All native output is private.
