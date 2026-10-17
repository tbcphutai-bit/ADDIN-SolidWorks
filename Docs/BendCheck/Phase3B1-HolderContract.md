# Phase 3B.1 — holder geometry and seating evidence

Issue [#10](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/10), baseline
PR #9 / `8e98774d4e39a8aaf3e9e37abc1ce058c9361585`. This extends the
[Phase 3B contract](Phase3B-PlacementContract.md); Check Min, session/capture,
WinForms/Designer/handlers, commands, Excel and candidate order are unchanged.
The existing managed file worker supplies these additional immutable results.
No SOLIDWORKS COM object is passed to it.

There are always **18 candidates per bend**, with `CHUA_XAC_MINH`, no winner,
no manufacturing PASS/NG and `ReadyForCollisionCheck == false`. This phase has
no sheet/tool collision engine, V-die model or feasible sequence search.

## Three independent contracts

1. **Drawing inventory**: exact saved source SHA, handle/record/instance path,
   block/INSERT transforms, raw/effective layers, linetype, source closure,
   analytic LINE/ARC and raw SPLINE data. An open or unsupported drawing entity
   is never silently removed or repaired.
2. **Reviewed material**: `HolderObstacleSet` has explicit contour/material
   definitions and conservative uncertainty bands. Drawing topology does not
   establish which side is metal. `GeometrySupportedForCollision` is separate
   from physical approval and does not run a collision check.
3. **Reviewed seating**: `ToolPairSeatingEvidence` binds that exact source,
   selected punch/holder, material definition, datums, frame and relative pose
   to an external authorized record. Coordinates alone are not approval.

`GeometrySelection2D.Curves`, `.Supported`, `.Region` and
`ToolGeometryPlacement2D.PlacedCurves` retain the previous analytic drawing
semantics. In particular, that raw subset does **not** become a complete
SPLINE holder obstacle. `PlacementGeometryComplete` remains the legacy drawing
inspection flag. New consumers must use `HolderObstacleSet` and
`HolderObstaclePlacement2D`, check freshness/readiness, retain the material
expression **and all bands**, and obey the future collision policy.

## Bounded SPLINE backend

`ToolDxfReader` preserves degree, flags, knots, weights, WCS control/fit points,
source normal and full nested INSERT/mm transform. SPLINE normal is metadata,
not ARC/LWPOLYLINE OCS. A zero normal on a linear SPLINE does not invalidate
actual XY WCS controls; nonzero control/fit/tangent Z is rejected. The original
punch reader still reports SPLINE unsupported, independently of the new holder
backend. LINE and ARC remain analytic.

`PositiveRationalBezierHullMmV1` performs homogeneous knot insertion, extracts
Bezier spans, then uses de Casteljau subdivision. Positive rational weights put
the curve in the Cartesian control hull. Each control coordinate is an outward
binary64 interval, including affine evaluation, weights, knot insertion and
rational division. The maximum control-box distance to a finite chord bounds
the **whole span**, rather than estimating error from samples. Each chord
retains its parameter interval and deviation bound in mm.

Default requested deviation is **0.001 mm** (allowed 0.000001–0.01 mm). Supported
degree is 1–7, at most 4,096 controls / 16,384 chords, subdivision depth 26 and
65,536 subdivision visits; coordinates are limited to 10,000,000 mm. Weights
must be finite, positive and have ratio at most 1e10. Knots must be finite,
monotone, correctly counted, nonempty and have supported multiplicity. A
nonrational flag with nonunit weights is rejected. Fit-only reconstruction,
nonplanar geometry, unresolved interval/denominator, discontinuities, invalid
closed/periodic endpoints, or exhausted numerical/work budgets return an
explicit unsupported result with **no partial chords**. Cancellation propagates.
There is no invented closing edge or smoothing of source geometry.

Bounds describe the interpreted saved binary64 DXF geometry/transform, not
physical manufacturing or metrology error. Unclamped periodic active-domain
end insertion uses the actual span to the right of that endpoint, rather than
the clamped shortcut that would move it. Synthetic tests use a separate Cox
basis evaluator. Local independent QA evaluates original entities and nested
INSERT matrices with ezdxf; sampling is a cross-check, not the proof of bound.

`ReviewedMaterialSetWithBoundaryBandV1` represents nominal material **union the
closed uncertainty band around every contour**, including holes. Nominal hole
subtraction must never discard those bands. Contour uncertainty includes the
largest member SPLINE deviation and observed numerical joint gap. Placement
retains the regions/holes and enlarges the bands under
`ConservativeRigidPlacementMmV1` for the rigid validator's numerical drift,
circle representation and affine floating-point evaluation. Work translation
outside ±10,000,000 mm remains unsupported, without geometry readiness.

## Multi-loop material rules

`HolderTopologyInventory` reports connected components, simple closed component
candidates, and open/branched components using 0.00001 mm endpoint clustering.
These are **inventory counts, not metal-region or hole counts**. It cannot infer
solid/hole roles from largest outline, nesting order, PHANTOM/BYLAYER or paper
position. Source linetype is retained only as evidence.

A `HolderMaterialDefinition` must bind `HolderBindingV1` and carry an authorized
`HolderMaterialRule` proof. Every selected source entity key must occur exactly
once in a contour or an explicit exclusion. Allowed exclusion roles are
`Decorative`, `Centerline`, `Reference`, `Annotation`; there is no implicit
ignore of inconvenient open/SPLINE geometry. Contour roles are supplied as
`Solid`, `Hole`, `Island`, with explicit parent IDs. Existing source entities
may be composed into a reviewed contour; no new edge is added and their raw
open/closed flags remain unchanged in the inventory.

Each contour must pass the existing analytic single-region validation after
bounded conversion, with joint gap at most 1e-10 mm. That validator's mm policy
remains unchanged: distance 1e-5 and area threshold
`max(1e-8 mm², perimeter * 1e-5 mm)`. Cycles, missing parents, invalid material
hierarchies and inconsistent containment fail closed. Within a component,
boundary contact uses both uncertainty bands plus the distance tolerance;
unresolved touching/collapsing holes are unsupported, not repaired.

- Default / `DisjointMaterialHierarchyV1`: independent solids must be disjoint.
- Explicit reviewed `UnionOfComponentMaterialsV1`: different root component
  materials may touch, overlap or nest. The output is a **set union expression**,
  not a guessed merged outline. Each component subtracts only its own holes;
  islands contribute their own material. Cross-component bands are also unioned.
  A hole in one component cannot remove real material from another component.

`RegionCombination == UnionOfEachOuterMinusItsOwnHoles` and placed
`MaterialRegions` preserve these semantics. No Boolean tessellation/collision
solver is introduced. Missing review/roles yield `UNSUPPORTED_HOLDER_FILL_RULE`;
bad coverage, unsupported boundary, overlap in strict mode, invalid containment
and stale binding have separate reason codes.

## Exact mapping and seating

A private `ToolHolderMappingRecord` binds exact label handle/text, root
INSERT/block roles and selected holder entity keys, plus source/manifest/
selection/frame/policy hashes. A signature cannot override mismatched current
selection keys. Ambiguous/reused instances remain `MAPPING_UNRESOLVED`.
There is no nearest-slot, INSERT-order, common-layer or bounding-box fallback.

`ToolPairSeatingRecord` is private input; `ToolPairSeatingEvidence` is an
immutable observation containing:

| Binding | Required information |
| --- | --- |
| Source | Tool ID, Master SHA, manifest SHA, selected punch SHA and holder SHA |
| Material | Exact reviewed holder material-definition SHA |
| Coordinates | Punch/holder source-frame IDs; reviewed holder-source → punch-source rigid transform; explicit mirror permission |
| Datums | Distinct tip, punch mount and holder mount; hash of approved local frame |
| Process references | Unit working/approach directions; contact reference and exact frame |
| Provenance | Payload/artifact SHA, reviewer/key ID, approval/expiry and authorized review purpose |

Missing numeric point/matrix fields are nullable and rejected; they never
default to an approved zero/origin. The source tip/mount/direction must match
the independently bound Master datum record. Transforming the holder mount
must reach the punch mount within 0.00001 mm. Datums are never inferred from
bbox, lowest point, drawing origin or final Part geometry.

Coordinate readiness requires current approved geometry and all matching
records. `ClampVerified` / `PhysicalPairVerified` additionally require a
**non-simulation physical inspection attestation**, an authorized key allowed
to attest physical inspection, and external mapping/material approval. A
simulation material proof cannot support a production seating claim. Production
does not generate keys/signatures or attest any actual company setup.

Complete synthetic/manual placement can have `GeometryCheckReady` for software
inspection. It remains `SimulationOnly`, has no production placement inferred
from a final Part, and still cannot return manufacturing PASS or collision
readiness. A source-selection success is not a verified physical assembly.

## External review and freshness

Optional private configuration fields (paths may be relative to that config):

```json
{
  "HolderEvidencePath": "holder-reviewed.json",
  "PairSeatingEvidencePath": "seating-reviewed.json",
  "ReviewAuthoritiesPath": "authorized-reviewers.json"
}
```

Holder document V1 contains `Materials` and `Mappings`; seating document V1
contains `Pairs`; authority V1 contains explicitly administered public `Keys`.
There are at most 18 exact-ID records per list. The authority file authorizes
reviewer/key/purpose, validity interval, simulation restriction and physical
inspection role. The add-in does not create or silently trust a colocated key.
Protect that file as company approval configuration; a signature proves the
configured authority approved a record, not that software observed a machine.

Proofs require RSA ≥2048 / PKCS#1 SHA256 and an existing artifact (at most 8 MB)
inside the private evidence directory. Signing uses `SignatureContent`, with
length-prefixed canonical hashes binding purpose, payload, reviewer/key,
approval/expiry, artifact content SHA and simulation/production intent. Numeric
hashes use invariant round-trip values. Actual inspection/signing is an external
authorized procedure; the test-only signer is not a production approval tool.

Every refresh reads saved hashes; observation fingerprints also include the
**actual artifact bytes** and verification result, so an artifact-only edit
cannot reuse an approved cached snapshot. Source, manifest, selection, material
policy, relative pose, datum, artifact, authority, proof and configuration edits
invalidate held source/candidate observations on the existing worker/poll
boundary. Held approvals also expire by time. No filesystem-lock or immediate
event guarantee is claimed. Corrupt optional evidence is isolated from punch
parsing; it cannot remove the 18 slots or modify Min. Missing coordinates,
stale hashes and invalid signatures fail closed with reasons.

Local `--holder-company` QA emits **UNFILLED private templates and a machine
operator checklist outside Git**. Only source facts/selection bindings are
prefilled. Measured datums/pose, physical confirmation, reviewer, dates,
signatures and public keys remain blank/false. Templates are not automatically
configured or approved. After assigning real material roles, recompute the
definition hash and complete independent inspection before signing seating.

## Sheet/Excel boundary

`BendSheetSection` remains strict for complete uniform extrusions, including
both skins and bend arcs. `SheetEnvelopeReadiness` explicitly requires future
multiple proven slices or native 3D evidence for axial variation/holes/trim.
Unsupported 2D is never an empty/non-colliding workpiece. No slices are invented.
Excel `MaxBendLength` and deep-clearance examples remain separate. Unreviewed
family-to-ID applicability remains `UNMAPPED_LIMIT_RULE`; no workbook is edited.

Reference: Autodesk's [SPLINE DXF contract](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm)
defines the WCS control points, flags, degree, knots and weights.
