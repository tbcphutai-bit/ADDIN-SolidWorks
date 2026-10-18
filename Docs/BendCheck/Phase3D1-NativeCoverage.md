# Phase 3D.1 — native plane/cylinder coverage

Issue [#16](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/16).
Baseline: `283a7a77a3280dda6cc36b82412608cf518cb58b` (PR #15).
The new branch targets Phase 3D and does not merge or modify older PRs/main.
See [observed validation](Validation-Phase3D1.md) for native before/after results,
failed attempts, test counts and limitations. All lengths below are mm.

## Actual rejection and scope

The failing body contains 42 planar faces, 14 cylindrical faces, 84 LINE edges
and 28 circular edges. There are four full-circle edges and four inner face loops;
no B-surface or unknown edge family is needed to describe this particular body.
The six bend frames belong to two orthogonal axis families.

The Phase 3D prismatic compiler stops at circular trims whose Z coordinate
changes. The first four frames have eight such edges, the last two have sixteen.
They are transverse bend rims, not missing or disconnected geometry. There are
also cylinders transverse to each inspected bend and full circular hole trims.
The old single-station/axial-edge restriction cannot describe them. The append
already had a complete closed shell, but its circular edge metadata omitted the
circle plane and full-circle edge sense needed by a general section family.

`OrthogonalNativeEnvelopeCompiler` is a direct geometry extension for those
families. The existing uniform and prismatic proof runs first, unchanged. Only
specific unsupported cross-axis/periodic cases enter the new compiler. It works
on the entire single owner body, not just faces near the inspected bend. A bend
frame changes the coordinate system, not which body surfaces are checked.

## Additional source evidence

The existing once-per-snapshot STA append now reads each circular edge's axis
and `CurveParamData.Sense`, directly from the same native curve. The new immutable
`NativeCircleDefinition` stores only edge ID, unit axis and sense. Source hash V2
includes them; earlier capture/placement hashes cannot silently reuse this proof.
All data crossing to the worker remains managed DTOs. No additional capture per
tool, model changes, suppression, rebuild or save is introduced by production.

The underlying circle plane is essential: a repeated full-circle endpoint and
its antipodal midpoint alone do not establish that plane. A missing plane is an
explicit UNKNOWN, not an inferred axis. Nonperiodic ARC orientation is recovered
from source endpoints, midpoint and plane; full-circle direction uses source
curve/edge sense. Directed coedges remain separate from curve parameter direction.
The official [CurveParamData.Sense contract](https://help.solidworks.com/2023/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ICurveParamData~Sense.html)
describes that distinction.

## Closed-shell proof

The compiler reuses Phase 3D manifold/loop validation and additionally verifies:

- Single, complete solid body; captured counts and opposite coedge incidence.
  No source edge or face is ignored because it is inconvenient to section.
- Every plane's complete outer/inner loops, analytic planarity, winding relative
  to the outward face normal, closed regions and hole hierarchy. It uses the
  existing analytic region validator; it does not close or snap an open loop.
- Each cylinder is an exact constant-radius patch bounded by two matching ARC
  rims and two axial generators, or by two full-circle rims. Rim centers, planes,
  radii, angular extent, generator direction and coedge conormals must agree with
  the cylindrical face's outward/inward sense.
- Plane normals and cylinder axes are parallel or perpendicular to the inspected
  bend axis. This admits a transverse quarter bend and a through cylinder, but
  does not pretend that a general oblique section is a circle or LINE.

Unknown curve/surface families, inconsistent trims/senses, unresolved small
features, missing circular evidence, unsupported oblique geometry and multibody
scope return UNKNOWN with reason codes and entity IDs where applicable.

## Exact sections and continuous enclosure

Every native edge endpoint and every attained **analytic axial extremum** of
each circular edge contributes a partition station. This includes an extremum
inside an ARC, not just its endpoint or sampled midpoint. Between these stations,
the admitted closed analytic patches have fixed section incidence/topology.

Planar faces contribute oriented LINE intervals, split correctly at all outer
and inner loop crossings. Cylinders parallel to bend Z contribute unchanged
analytic ARCs. For a cylinder perpendicular to Z, its intersection branches use
`h(z) = sqrt(r² - d(z)²)` and contribute bounded LINE segments between the two
source rims. Face sense supplies the material orientation. The existing
`CollisionComponent2D`/hole semantics and region validation construct material,
voids and islands; LINE/ARC geometry is retained.

For a complete axial interval, every moving endpoint has an outward interval
bound. LINE interpolation bounds are affine; transverse circular roots propagate
radius, center/height and arithmetic uncertainty through the square root.
The maximum endpoint displacement bounds the whole LINE segment. Parallel ARCs
are invariant. The admitted analytic section family cannot change material away
from these moving boundary bands between source events. Thus the nominal section
**plus all boundary bands**, including hole boundaries, encloses every true
section over that interval. A representative section alone never grants CLEAR.

`SliceFamily.Section` returns actual nominal section geometry with propagated
point uncertainty. `Enclose` returns the full-interval material superset. It plugs
directly into the Phase 3D `SheetEnvelopeCapture` and `AxialCollisionEngine`; the
Phase 3C LINE/ARC/SPLINE collision kernel is not rewritten. Intersection with an
enclosing band alone is UNKNOWN; a COLLISION still needs certainly occupied
source material at a common verified tool axial coordinate.

## Numerical policy

| Quantity | New native-family policy |
| --- | --- |
| Base native source allowance | Existing `1e-5` |
| Family allowance | `4e-5` for native/frame/alignment error, propagated through roots and endpoint bounds |
| Loop joint and matching trim consistency | `1e-10`; no geometry repair |
| Accepted axis alignment | `1e-12` from parallel/perpendicular |
| Native input/frame magnitude bound | `1e6`, checked on edge points, arc centers/midpoints, surface anchors and frame origin |
| Maximum supported circular radius | `1e4`; radius must exceed family allowance |
| Minimum resolved axial slab | `4 × family allowance = 1.6e-4`; otherwise UNKNOWN |
| Collision policy and budgets | Existing Phase 3C/3D policy, unchanged |

Error is **not** a constant small XY band near a tangent. Root sensitivity can
amplify a small source error; the interval calculation preserves that larger
band. A focused test near a cylindrical extremum returns UNKNOWN instead of an
unjustified interior witness. Cap/event uncertainty also remains in the axial
margin. Very large or unresolved bands may prevent a collision decision even
when the analytic envelope is available. No UNKNOWN is promoted to CLEAR.

## Native reference and regression behavior

Native coverage is tested against an independent STEP/OpenCascade B-rep classifier
using private source copies. The reference validates one closed solid, checks
units/frame against SOLIDWORKS exact extrema, compares material and void points,
checks interval enclosure inclusion, and independently checks actual collision
witnesses. Synthetic independent analytic volumes exercise the newly admitted
native-style bore and quarter-bend shells. These sampled QA comparisons support
testing; they are **not** the production continuous-coverage proof.

Reference STEP exports, coordinates, CAD hashes and raw diagnostics stay outside
Git. OpenCascade/VTK are isolated QA dependencies, never add-in dependencies.
The initial SOLIDWORKS ray-reference attempts failed with a COM server exception
and are disclosed; they are not treated as verified ray results.

An inherited bad-manifest test compared serialized JSON strings. A reproduced
failure retained different object member order but identical parsed Min/Planning
values and candidate arrays. Test-only `NativeSnapshotJson` canonicalizes object
keys while preserving every value and array order. Production Min/Planning code
is unchanged; old historical failures without exact strings remain qualified.

## Readiness and remaining limits

The real six-bend Part now obtains `AxialSlicesWithCertifiedCoverage` for all six
frames. The three-bend uniform Part stays `Uniform2DProven`. This is sheet envelope
readiness, distinct from production tool/collision readiness. All 18 real tools
still lack approved finite occupancy/mount data and remain UNKNOWN.

General oblique cylindrical/elliptic trims, cones, spheres, B-surfaces, irregular
cylinder parameter patches, unsupported small features and multibody unions need
further proof/backends. No `VerifiedNative3D` tier is emitted. Before/during/after
forming states, contact rules, feasible sequences, tool ranking and a whole-Part
manufacturing PASS remain outside this phase. No V die is checked.

The current WinForms/Designer/event handlers, Check Min, candidate-order logic
and commands are untouched. Native coverage is exposed through the existing
Phase 3D managed worker API; this is not a claim that the legacy UI has adopted
that worker or that its older single-section result has been rewritten.
