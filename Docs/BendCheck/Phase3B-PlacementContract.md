# Phase 3B — read-only 2D placement foundation

Issue #8. Baseline: PR #7, `7f2f49a6cbea1ce559d2c0ee94bd0176ab0b04d7`.
Branch: `feature/check-chan-phase3b-placement`; PR base is
`feature/check-chan-phase3a-geometry`. No dependent PR is merged.

This phase prepares source selections, coordinate transforms and sheet coverage.
It has no collision engine, V-die check, tool winner or feasible bend sequence.
Every manufacturing conclusion remains `CHUA_XAC_MINH`.

## Snapshots and integration

`ToolProfileLibrarySnapshot.PlacementSources` supplies 18 `ToolAssemblyProfile`
slots and source-backed `BendLimitRuleDraft` references. `BendCheckItem` adds a
`PlacementModelCapture` and `PlacementDraft`; all existing Min rows/results,
candidate orders, commands, WinForms/Designer and handlers are retained.

The only append in `BendPanelGeometryReader` reads circle center/radius and an
actual midpoint while its existing `Curve`/`CurveParamData` are available on
STA. Failure produces missing placement metadata, without changing Min edge
kind/endpoints or its Error. No second body traversal or kernel section is run.
The midpoint is evaluated on **IEdge**, with its packed success status checked.
Reverse-sense `CurveParamData` can use negative parameter space; sending that
edge parameter to the underlying ICurve can select the complementary arc.
See the official [UMaxValue parameter-space remarks](https://help.solidworks.com/2020/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ICurveParamData~UMaxValue.html)
and [IEdge.Evaluate2 contract](https://help.solidworks.com/2016/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IEdge~Evaluate2.html).
After the existing capture, `BendPlacementNativeCapture.Copy` copies managed
body/face/edge/feature data. Library changes rebuild only managed candidates.

The existing file worker reads DXF, manifests, datum evidence and Excel.
It never receives a COM object, row/context, control or model. Computational
geometry DTOs and transforms have no COM dependency. The owner freshness closure
reads captured managed flags only; STA polling owns stamp/configuration checks.

`PlacementDraft.Current` combines model lease, owner/context flags, library and
placement-source leases. A new source snapshot invalidates held old drafts;
rebuild/configuration changes hide stale item drafts and use existing rescan.
Freshness is observed at the existing polling boundary, not a filesystem lock or
an immediate model-event guarantee. No production consumer may bypass Current.

## Master selection and provenance

An optional **private** `MasterAssemblyManifest` V1 pins `MasterSha256` and
lists exact company IDs. Each entry requires a model-space identity TEXT handle,
its exact text containing a complete ID token, and explicit root INSERT/block
keys plus separate punch/holder selectors. No nearest-frame, INSERT order,
largest-outline, bbox, lowest-point or INSBASE inference is used.

`GeometrySelection2D` retains source SHA, selected entity/instance keys, expanded
analytic LINE/ARC curves and each original instance transform. All selections
from the same saved Master are in `MasterWcsMm:<sha>`. Source closure and region
validation remain separate. Annotation/paper/decorative geometry is rejected;
duplicate/array root ambiguity or overlapping roles are explicit failures.
Unknown/missing slots remain in the 18-entry result.

`IdentityMatched` identifies the tool label. `MappingIdentified` additionally
requires both selected roles; neither flag verifies physical mounting or their
relative seating. Holder geometry remains ReferenceOnly. SPLINE is retained as
unsupported metadata; no tessellation, automatic closure, union or filled bbox.
Open/multiple regions remain unverified/unsupported.

DIM-file datums are not borrowed for the Master. Optional Master datum evidence
uses the Phase 3A separate file/artifact/source/selected-geometry bindings against
the selected **Master** punch. `VerifiedRecord` still means an external record
matched its contract, not physical inspection. `ClampVerified` and
`PhysicalPairVerified` remain false in this phase.

## Frames and placement math

- All geometry lengths are mm; native metre conversion is explicit at capture.
- A `BendWorkFrame` is right-handed: X/Y lie in the section plane, BendAxis is
  the normal along the observed bend. Origin is the observed bend-axis midpoint.
  This computational origin is not a punch datum or known contact point.
- The source model supplies no verified machine approach. The final folded
  model does not identify which side moved during a manufacturing sequence.
- `PlacementTransform2D` permits rigid orthonormal transforms only; determinant
  must be +1, or -1 with explicit mirror permission, within 1e-10. No scale/shear.
  Translation is mm. `parent.Then(local)` means parent(local(point)). Inverse,
  directions, circle radius and reflected sweep are preserved analytically.
- `Align` requires a supplied source tip/working direction and target
  contact/approach. Tip and mount remain separate. Different source frames need
  an explicit from/to registration; shared DXF origins are never assumed.

`PlacementSimulationRequest`/`PlacementRegistration2D` provide a tested manual
inspection path. They, and synthetic sheet captures, are always SIMULATION_ONLY.
No production placement is inferred when contact, approach, datum, registration
or pair evidence is missing; ToWorkFrame remains null with reason codes.

`PlacementGeometryComplete` describes mathematical inspection data only. It
requires current data, both roles, supported single closed regions and no joint
gap above 1e-10 mm; it is not physical verification. Phase 3B does not issue
READY_FOR_COLLISION_CHECK: ReadyForCollisionCheck remains false. Enabling that
later requires a reviewed physical pair/contact/approach contract in addition to
geometry; collision testing still cannot imply manufacturing PASS by itself.

## Full sheet section and limits of 2D

`BendSheetSection` uses the **entire actual material boundary**, including both
skins, inner/outer bend arcs, returns and back walls. Min's A/B/C windows or
L/Z/C labels are never used as a substitute silhouette.

The initial supported class is deliberately strict: a complete single body
uniformly extruded along that bend axis. Planar/cylindrical surfaces, full end
cap topology, axial edges and trim positions must prove this class. Distance
tolerance is 1e-5 mm; parallel/perpendicular checks use 1e-8. Curves are projected
from the complete end-cap boundary into the perpendicular work frame; no general
3D projection claims are made. Analytic arc center, radius and midpoint determine
the actual sweep. Missing arc metadata never becomes a chord.

Internal trim/tab/relief, cross-axis holes/cylinders, lengthwise changes,
unassigned/multiple bodies, unsupported surfaces/curves or invalid/multiple
regions produce UNSUPPORTED_2D_ENVELOPE. A failed proof exposes no partial
boundary as a complete collision region. This class can reject parts for which
a future conservative multi-slice/3D envelope could be built.

Each candidate has UnknownBefore, UnknownDuring, UnknownAfter and a separately
labelled FinalModelObserved state. No unfold, suppress, rebuild, configuration
switch or save is performed by the production adapter. Existing candidate bend
orders keep their original unverified meaning.

## Excel evidence, independent of Check Min

An optional private `BendLimitManifest` V1 pins workbook SHA and each rule's
ID/family/kind/sheet/cell or drawing object/unit/tool IDs. Optional object SHA
pins source transcriptions. MaxBendLength and DeepBendClearanceExample are
different kinds. Readable numeric source facts may be retained even when tool
applicability is unmapped. References are not executable manufacturing rules.

The read-only OPC/XML parser preserves shared/inline text, drawing IDs and media
hashes. It does not use Excel COM, OCR, cached formulas, external links or macro
execution. Boolean/error/ISO-date cell types are rejected. Numeric cells are read
as stored; display formats (including date-formatted numeric serials) are not
interpreted and cannot establish a unit or rule kind. Explicit mm/m conversion
and an optional bounded regex with a named `value` group transcribe a number;
unreadable ranges/images/units remain unknown. No company length/example number
becomes a default limit for individual tools.

FamilyMappingVerified remains false: a private source-selection manifest alone
cannot authenticate applicability. Missing, stale or ambiguous rules produce
UNMAPPED_LIMIT_RULE. These sources do not alter the existing Min Excel settings,
schema, evaluator or data. Workbook limits: 32 MB file/entry, 128 MB total expanded
entries, 4096 entries, 20000 source objects; XML DTD and external retrieval disabled.

## Configuration and diagnostics

Set process `ADDIN_BEND_PLACEMENT_CONFIG` or add `PlacementConfigurationPath` to
the existing private DXF source config. The existing DIM source directory is
still required by its provider; placement parsing can be tested independently.
No new setting UI is added. Configuration V1, using **placeholder paths**:

```json
{
  "Version": 1,
  "MasterPath": "C:\\Company\\Master.dxf",
  "MasterManifestPath": "master-selection.private.json",
  "MasterDatumEvidencePath": null,
  "LimitWorkbookPath": "C:\\Company\\Limits.xlsx",
  "LimitManifestPath": "limit-selection.private.json"
}
```

Companion paths resolve relative to the placement config directory. No source
file, companion map or setting is auto-created beside company data. Source
bytes/hash changes, mapping changes and evidence changes affect fingerprints.
Missing placement sources do not block Check Min. Diagnostic failures have a
separate PlacementDiagnosticError and cannot write the Min Error.

`[BEND PLACEMENT DRAFT]` contains only status/counts/tool IDs/reason codes, without
CAD coordinates, matrices or model names. Full DTOs, source inventories, private
selection manifests and native/geometry reports must stay outside the checkout.
No company CAD, Excel, SLDPRT, design coordinates or private mappings are bundled.
