# Fixed company Punch–Holder CAD pairs

This extends PR #32 without changing Check Min math, the WinForms Designer,
command registration or company CAD. The existing Check Chấn Part/Assembly
pipeline, 18-candidate inventory and Phase 4B planner consume one shared catalog.

## Local source configuration

`%LOCALAPPDATA%\ADDIN\bend-tool-dxf-source.json` may select this mode:

```json
{
  "Version": 1,
  "UseMasterPairsAsLibrary": true,
  "PlacementConfigurationPath": "C:\\LocalEvidence\\placement.json"
}
```

The placement configuration uses the existing schema. Set `MasterPath` to the
company DXF and `MasterManifestPath` to the private version-2 fixed-pair manifest.
Do not copy internal CAD or coordinate-bearing evidence into this repository.
`DirectoryPath` and the 18 separate DIM files are not required in this mode.
There is no fallback from a failed Master selection to a different Punch source.
Legacy version-1 DIM/placement configurations continue to work as before.

For isolated validation, set `ADDIN_BEND_USE_MASTER_PAIRS=1` and
`ADDIN_BEND_PLACEMENT_CONFIG` to an absolute placement configuration path.
An explicit `ADDIN_BEND_TOOL_DXF_DIRECTORY` retains its existing environment
override behavior; set the master-mode flag as well or remove that override.

## Version-2 manifest contract

The file contains `Version: 2`, the exact `MasterSha256`, and exactly one `Tools`
entry for each of the existing 18 `CompanyToolIds`. Each entry supplies:

- `Id`, exact `LabelHandle` and `ExpectedLabel` from the source TEXT record.
- `PunchRootInsert`, `PunchBlock`, `Punch` selector.
- `HolderRootInsert`, `HolderBlock`, `Holder` selector.
- `PresentationPair.SlotId`, `AssociationBasis: CompanyAuthorizedDrawingSlot`,
  and a nonempty `EvidenceReference` recording the company authorization.
- `PresentationPair.FrameHandle` and `FrameLayer`: the explicitly drawn,
  closed rectangular model-space LWPOLYLINE presentation cell.
- `PresentationPair.Punch` and `.Holder`: exact `RootLayer`, complete effective
  `Layers` set and `EntityCount` of every non-annotation source entity below that
  root INSERT, including unsupported geometry and reference lines.

The reader validates these **declared** identities. It never searches for the
closest holder, sorts INSERTs into pairs or substitutes a similar block. Source
INSERT positions are checked against the explicitly selected presentation cell
to reject a same-block holder swapped into another slot. This is a presentation
association check only; it derives no physical origin, seating or relative pose.
Repeated block definitions are allowed; repeated root instances or frame/slot
identities are rejected. Omitting a troublesome entity fails the full-inventory
check. The source hash pins every DXF record, including labels and transforms.
Version 2 additionally binds each Punch to this company's `TOOL_<tool ID with
underscores>` block naming convention. A correct TEXT label elsewhere cannot
validate a different entire Punch/Holder/cell triplet under the wrong tool ID.

The private manifest is authored once from the labelled CAD cells. A changed
DXF hash requires explicit rechecking/re-pinning of that manifest; it does not
trigger automatic geometric reassociation.

## Geometry and evidence boundaries

The existing DXF reader still owns all geometry interpretation. Source LINE,
ARC, polyline closure/bulges, NURBS control points/knots/weights and nested INSERT
transforms remain intact. The new root inventory adds presentation metadata; it
does not normalize, recenter, repair or retessellate any entity. A source WCS is
named `MasterWcsMm:<SHA256>`; a CAD pair reports `DXF_PRESENTATION_WCS_ONLY`.

`CadPairBinding.Matched` means the fixed drawing association was corroborated.
Its status is `CAD_REFERENCE`. It does **not** set `MappingReview.RecordVerified`,
`GeometryCheckReady`, `ClampVerified`, physical datum/seating or tool length.
Existing signed material/mapping/seating evidence remains independent. Raw
multi-outline holders are retained for inspection; material roles are not
inferred by choosing the largest loop or treating every line as solid material.
An open KIK Punch stays open and cannot form a valid material region.

The existing UI shows the same 18 rows. Selecting a row exposes CAD Punch,
Holder and mounting evidence in the existing evidence area; source SHA, slot
and block/INSERT/layer identities are retained. The guarded Assembly read
snapshot carries these managed-only references; no COM is sent to a worker.

## Explicit CAD-reference process simulation

`ProcessToolRecord.CadReferenceSimulation` is an optional Phase 4B input. It
references **the catalog's actual selection**, never replacement fixture curves.
It requires:

- Exact `SourceSha256`, `ManifestSha256`, `SelectionGeometrySha256`.
- Explicit rigid `SourceToWorkFrame` values (A/B/C/D, TxMm/TyMm and approved
  mirror flag), with no automatic tip/seating alignment.
- `AxialSectionContract: ExplicitSimulationUniformProfileOverListedSpan`,
  `StartMm`, `EndMm`, `BoundaryErrorMm` and `AxialErrorMm`.
- A current trusted **simulation-only** review for purpose
  `CadReferenceSimulation`. Its payload binds native model/configuration/stamp,
  FeatureId, sheet envelope, source pair, pose, axial interval and error bounds.

The Punch must have a valid unmodified closed analytic region. A Holder also
needs the existing reviewed material definition, including excluded reference
entities, hole/solid roles and every SPLINE uncertainty band. Missing evidence
produces a named rejection/UNKNOWN; it does not invent physical setup data.
`Payload()` exposes the canonical hash to an external reviewing workflow; the
production application never signs its own approvals.

The existing motion/collision engines compute the transformed simulation copy;
the catalog geometry is not changed. These results remain Simulation, never a
manufacturing certification. Native bend review identities stay separate while
equivalent source hardware and placed volumes can share the whole-Part tool
identity. The existing preference remains one complete proven common-tool
route, then minimum tool changes. No new multi-tool changeover search is added.

With a fixed CAD catalog active, the old arbitrary `SimulationGeometry` tool
replacement path is rejected. It remains available for legacy controlled
software fixtures that do not declare a company fixed CAD pair.

## Validation entry points

Build `Tests/BendToolProfiles/ToolProfiles.Tests.csproj` and run:

```text
ToolProfiles.Tests.exe --cad-pair-tests
ToolProfiles.Tests.exe --cad-pair-inventory <private-source.dxf> <private-inventory.json>
ToolProfiles.Tests.exe --cad-pairs-company <private-placement.json> <private-output-folder>
```

The inventory command exports identities/counts only and never generates pairs.
The company check verifies all 18 associations, full source inventories and
unchanged source hashes. It also swaps two actual same-block Holder INSERTs in
a **separate private manifest**, proves they are rejected by their fixed cells,
and confirms the other 16 associations remain valid. It never edits the source
DXF or the live mapping. Company reports and manifests remain local.
