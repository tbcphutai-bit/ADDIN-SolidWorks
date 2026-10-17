# Phase 3C — managed 2D collision contract

Issue [#12](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/12), based on
PR #11 commit `9f2f6bdb90974c727289de4cbaef69d7c77a42e5`.
Implementation: `Commands/BendCollision/`. Target framework remains .NET 4.8,
C# 7.3, x64. There is no geometry package, COM, WinForms or file-reader
dependency in the collision kernel.

## Executable path

The existing `BendPlacementBuilder.Build` now evaluates every
`ToolCandidateDraft`, retaining the established bend order and all 18 tool IDs.
`PlacementDraft.CollisionResults` exposes the resulting rows. No new command,
session traversal, UI column or manufacturing verdict is added.

Each row has independent `PunchResult` and `HolderResult`, `CombinedResult`,
four explicit state results, tool/feature IDs, simulation marker and reasons.
`FinalModelObserved` is the only captured state. `UnknownBefore`,
`UnknownDuring`, `UnknownAfter` are evaluated as missing states, never copied
from the final model. `AllRequiredStatesResult` cannot be CLEAR while any
required state is unknown. `ManufacturingStatus` stays `CHUA_XAC_MINH`,
`SequenceVerified` is false and `WinnerToolId` stays null.

For either a state or the combined pair:

| Punch | Holder | Combined |
| --- | --- | --- |
| COLLISION | any | COLLISION |
| any | COLLISION | COLLISION |
| CLEAR | CLEAR | CLEAR |
| otherwise | otherwise | UNKNOWN |

A collision identifies overlap at that observed pose. Even a CLEAR observed
pose does not establish insertion, withdrawal, bending motion or a feasible
manufacturing sequence. V dies are outside this engine's inputs and scope.

## Material geometry and conservative uncertainty

`CollisionMaterial2D` is an explicit union of components; each component is its
outer material minus **its own** holes. An island is another solid component.
Roles are supplied, not inferred from drawing order, largest area, orientation,
bounding boxes or containment alone. Existing analytic region validation is
reused, with an additional analytic endpoint consistency check. Empty/open,
self-crossing, degenerate or invalid component/hole hierarchies cannot be CLEAR.
No repair, snapped endpoint, new closing edge or modified DXF is emitted.

For Punch, the adapter requires an observed-closed, supported, valid source
region. The source-open KIK punch is UNKNOWN. For Holder, it consumes the
complete reviewed `HolderObstaclePlacement2D.Contours` and `MaterialRegions`,
with the exact `UnionOfEachOuterMinusItsOwnHoles` expression and every placed
`BoundaryBandRadiusMm`. Raw holder `Curves` are not a collision envelope.

The nominal analytic material plus every contour's uncertainty band is the
conservative occupied envelope. The certainly occupied interior excludes every
band around the relevant outer/hole boundaries. This handles SPLINE chord
error, placed geometry error and numeric policy separately from nominal fill.
Adding uncertainty cannot establish a new CLEAR result. Unreviewed material
roles or unsupported curves remain UNKNOWN; parsing a SPLINE or having
coordinates does not establish physical verification.

## Numerical policy and actual algorithm

Policy `AnalyticMaterialBandsMmV1`, in mm:

- Default geometric tolerance `1e-5`; supported range `1e-7` through `0.1`.
- Maximum work 250,000 steps, 1,000 ms and 36 subdivision levels per pair by
  default. Caller policy is bounded at 2,000,000 steps / 10,000 ms / 48 levels.
- Analytic/source endpoint consistency and maximum source joint gap must be at
  most `1e-10`. The numeric guard is `8 * max(1e-9 + 2e-12 *
  (1 + curveRadius + maxAbsoluteCurveCoordinate))`. This guard is included in
  separation thresholds/clearance bounds and certain-interior margins.
- Binary64 intervals widen after every arithmetic operation, square and square
  root. Interval division through zero and nonfinite values fail closed.
  Rotations used for bounds/arc predicates use 32-term interval Taylor
  sin/cos at absolute angle at most 7, plus an explicit `1e-24` remainder.

The kernel performs these geometric operations:

1. Obtain enclosing boxes for whole materials. They may avoid redundant
   containment probes. They never replace the per-curve narrowphase.
2. Compute analytic LINE/LINE, LINE/circle and circle/circle feature hints,
   including segment intersections, directed CW/CCW arc parameters, tangent
   candidates and nearest endpoint/projection features. These seed upper
   distance estimates and possible overlap witnesses; they do not prove CLEAR.
3. For **each** cross-material curve pair, use a best-first interval
   branch-and-bound distance solver. LINE parameter bounds use outward affine
   intervals. An ARC subinterval is enclosed around an interval-evaluated
   midpoint with its complete arc-length reach; it is never replaced by sampled
   chords. A full circle initially uses center +/- radius. Subdivision tightens
   the boxes. The minimum of the live node lower bounds certifies separation
   only when it exceeds both contour bands, numeric guard and tolerance.
4. Classify material points with interval ray crossings. Integer ray directions
   avoid floating trigonometric direction choices. LINE intersections and
   circle discriminants are interval evaluated. Partial-arc membership uses
   interval cross products and the directed minor/major sweep. Ambiguous
   vertices/tangencies choose another ray; failure of every ray is UNKNOWN.
   Point-to-segment distance uses interval projection/cross products;
   point-to-partial-arc distance conservatively uses its supporting circle.
5. To return COLLISION, find a point strictly inside certainly occupied material
   of **both** inputs, farther than tolerance from all relevant boundary bands.
   The result exposes point, conservative interior disk margin and the two
   component IDs. Centers/intersection-normal probes only search for such a
   witness; the predicates must still prove it. Failure to find a witness is
   UNKNOWN, never proof of no collision.
6. To return CLEAR, first certify every cross-material boundary separation,
   then rule out material containment in both directions, including every
   connected outer/hole/island contour. A representative point on a connected
   contour is used **only after continuous boundary separation is proven**.
   This detects entire containment even when no boundaries intersect.

`CertifiedClearanceLowerMm` is a conservative lower bound, not an exact minimum
distance. It is non-null only for a current CLEAR result. The separately named
`NominalBoundaryDistanceUpperMm` is a feature-witness upper estimate on nominal
boundaries, not an upper bound on physical clearance with source uncertainty.
Witnesses are available only for current COLLISION results.

Touching, coincident-edge contact without proven interior overlap, tiny gaps,
band-only overlap, numerically unresolved topology/predicates, budget/depth/time
limits and cancellation return UNKNOWN with a specific reason. This is a
conservative decision procedure; some real overlaps can remain UNKNOWN.

## Placement evidence and freshness

`PlacementCollisionEvaluator` uses existing complete `SheetSection2D`, punch
placement, full holder material placement and reviewed relative seating.
Unsupported trim/holes/axial variation require measured multiple slices or
native 3D in a later phase; partial Min silhouettes never replace a full sheet.
Frame IDs, tool identity, datum and contact/approach are checked independently.

Manual `PlacementSimulationRequest` is exclusively simulation. Its explicit
tip/direction must match the source datum; its contact/approach/frame must be
supplied. The reviewed relative holder-to-punch transform is applied, including
the previously provided SPLINE placement uncertainty. Neither paper placement,
the lowest point nor a default origin is a datum.

`BuildWithProductionPlacements` is a separate typed entry for future external
configuration integration. `CollisionWorkPoseRecord` binds tool/feature,
model snapshot/update stamp/configuration, complete section hash, work frame,
Master hash, exact seating payload, observed state, explicit contact and unit
approach direction, mirror authorization and external review. It reuses
`ToolReviewVerifier` with purpose `CollisionWorkPose`. A production model,
physically verified pair and authorized non-simulation record are all required.
No local signature, approval, physical measurement or default work pose is
generated. The evidence also binds to the exact captured model/library objects;
it cannot be reused for another Part with a coincidentally matching feature ID.
There is no new settings UI or automatic evidence file loader in this phase.

Model/source/evidence/authority leases are checked during computation and on
held results. Invalidated inputs hide old clearance/witnesses and turn the
result into UNKNOWN. Punch and holder evidence stay independent where the
source contracts permit it: an unknown holder does not erase a certified punch
collision. Production poses necessarily depend on their approved pair evidence.

The managed kernel can run on a worker and is tested that way. Existing
SOLIDWORKS capture and command/session execution remain STA; no COM object is
sent to a worker. Input material validation is a preparation step; the solver's
budget covers collision work on prepared immutable inputs. The existing
placement builder evaluates synchronously; parallel batch scheduling for many
fully approved expensive candidates is not added to the UI in this phase.

## Next phases and references

Phase 3D needs actual material roles/mapping/datum/setup/contact approvals and
any required multi-slice/native-3D coverage. Phase 4 must generate and validate
all relevant process states, insertion/withdrawal and feasible sequences before
ranking one common tool for the whole Part. Observed geometry results, Excel
references and manufacturing feasibility remain distinct contracts.

The material expression follows standard regularized solid/holes semantics;
see [CGAL Boolean set operations](https://doc.cgal.org/latest/Boolean_set_operations_2/index.html).
CGAL is not a dependency. Independent optional QA uses
[Shapely/GEOS](https://shapely.readthedocs.io/en/stable/manual.html), with a
declared arc tessellation allowance only in the oracle; production stays
analytic. See [Phase 3C validation](Validation-Phase3C.md) for observed counts
and the distinction between synthetic certificates and unverified company data.
