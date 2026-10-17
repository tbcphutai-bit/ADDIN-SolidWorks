# Check chấn Phase 3D — continuous axial coverage and managed batch

Issue [#14](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/14), based on
Phase 3C `0f57fd2de331baebaaba1d28ab9bfeb7dc1e4ac1`. All distances are mm.
This extends the existing analytic collision kernel with a working certified
subset of 2.5D solids. It does not implement a general native 3D Boolean engine,
a forming sequence, a tool winner or a manufacturing PASS.

## Source and coverage contracts

`SheetEnvelopeCapture` binds the exact `PlacementModelCapture` instance, snapshot
ID, configuration, update stamp, body/feature identity, work frame, full analytic
source SHA256 and live capture lease. Its arrays and geometry are immutable.
Changing a model/configuration or invalidating its existing placement lease
makes held coverage and collision certificates unusable.

| Tier | Evidence and current implementation |
| --- | --- |
| `Uniform2DProven` | Reuses the existing complete uniform extrusion proof, including LINE/ARC boundaries; finite source axial extent is retained. |
| `AxialSlicesWithCertifiedCoverage` | A complete closed native prismatic shell partition, or an explicitly controlled analytic solid definition. Every interval has a full enclosing material set. |
| `UnknownCoverage` | Missing, incomplete or unsupported topology; exact observations alone cannot restore continuous coverage. |
| `VerifiedNative3D` | Reserved only. No code in this phase produces this tier. |

`AxialSlice2D` is an exact source-derived observation at one axial coordinate,
including solid/void roles, source identity, boundary error and distance from
source event/cap uncertainty. It does **not** stand for a finite axial interval.
`EnvelopeCoverageCertificate.Intervals` separately records the complete source
partition. `ObservationsOnly` deliberately discards the coverage proof: two or
many clear observations cannot become a global CLEAR. An observed slice can
prove COLLISION only with certainly occupied sheet material and verified tool
occupancy at that same coordinate, away from uncertain caps.

### Native supported subset

`PrismaticEnvelopeCompiler` consumes an additional complete, oriented B-rep DTO:
face surface kind/parameters/sense, every face loop and directed coedge, all
edge incidences and analytic LINE/ARC geometry. It checks capture counts, solid
body scope, two opposite coedge uses per edge, loop closure, face/trim agreement
and cap orientation. Multiple bodies are rejected; assembly instances are
handled through the existing isolated Part/configuration captures.

For the new subset, side walls are invariant parallel to the bend axis between
**all** source edge event stations; caps are perpendicular to that axis. LINE
edges must be axial or at a single station. Supported cylindrical side patches
must be parallel to the axis with matching analytic rims. Thus material cannot
change unnoticed inside a partition interval. A midpoint section is used to
construct the invariant interval material **after** that proof, not as evidence
from sampling. Oriented section loops determine outer material, inner voids and
islands; existing region/material validation checks their relationships.

This supports right-prismatic holes/slots, axial steps, local pockets, trimmed
notches and intermediate tabs when the entire captured shell satisfies these
restrictions. It does not infer surfaces or fill missing loops. Sloped faces,
nonprismatic trims, native cross-axis bores, B-surfaces, unsupported curves,
singular/nonmanifold loops, unresolved thin features and ambiguous scope return
specific UNKNOWN reasons. Full periodic cap-circle orientation is explicitly
unsupported by the **new** shell compiler; the existing uniform path is retained.

### Controlled analytic solids

`AnalyticSheetEnvelope.Controlled` defines a complete synthetic volume and always
marks it simulation-only. An unrelated model is only an identity anchor, never
proof that an actual Part has those dimensions. Supported definitions are:

- Unions of exact prismatic material sets, with holes/islands and local axial
  extents. These exercise stepped, staggered, pocket, notch and tab geometry.
- Full analytic LINE/ARC sections translated linearly along the axis. The swept
  interval is bounded by the complete displacement disk, including every hole
  boundary. Sampled displacement maxima are not used.
- A box minus an X-directed through cylinder, perpendicular to bend Z. Exact
  sections use `sqrt(r² - (z - z0)²)`; a full interval subtracts only the smallest
  void proved across that interval, giving an enclosing material set. This is
  implemented analytic cross-axis variation, not native bore recognition.

These produce real CLEAR/COLLISION/UNKNOWN results and independent solid-reference
checks. They are not substitute company tools or approvals.

## Read-only native capture and orientation

`SheetNativeTopologyCapture` runs on STA, appends one whole-body topology read
and caches it on the original Min context for the snapshot/configuration/stamp.
`BodyCaptureCount`, `TotalCaptureMilliseconds` and `WrongThreadCount` are observable
counters. Reusing that context for changed library data does not recapture COM.
The original Min `GeometryReadCount` remains 1. This is an additional read, not
a claim that the old partial snapshot already contained a complete B-rep.

Native pointers are used only transiently on STA to build integer incidences;
no COM objects, pointers or native geometry callbacks enter `NativeEnvelopeBody`
or the worker. Capture does not change configuration, suppress features, rebuild
or save. The adapter checks the item/configuration/stamp again after capture.

The append normalizes curve-oriented endpoints before applying the coedge sense:
when `ICurveParamData.Sense` is false, the parameter endpoints are reversed with
respect to IEdge. This corrects the new topology capture, leaving Min endpoints
and its existing reader unchanged. See the official
[CurveParamData.Sense contract](https://help.solidworks.com/2023/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ICurveParamData~Sense.html)
and [EdgeInFaceSense contract](https://help.solidworks.com/2024/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IEdge~EdgeInFaceSense.html).
Face normals account for
[FaceInSurfaceSense](https://help.solidworks.com/2025/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFace2~FaceInSurfaceSense.html);
loop direction follows the captured
[Loop2.GetEdges ordering](https://help.solidworks.com/2021/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.ILoop2~GetEdges.html).

## Finite Punch and Holder occupancy

`ToolAxialSetupRecord` is a new typed external evidence contract, independent for
`PunchAxialSetup` and `HolderAxialSetup`. It requires:

- Exact tool/feature/model snapshot/stamp/configuration, envelope geometry hash,
  work frame, Master/profile hash and seating payload hash.
- Explicit finite local segment start/end values, an axial origin, axis sign
  `+1` or `-1`, bounded axial error and the reviewed statement
  `UniformProfileOverListedSegments`. A signed unsupported contract is rejected.
- Explicit contact/approach and mirror approval for the XY pose, plus the existing
  tip/direction and relative Punch–Holder seating records. No origin, bounding
  box, lowest point or Excel example supplies a missing datum or length.
- A current purpose-specific review, verified by the existing external review
  authority mechanism. A production record also requires physical pair evidence
  and a nonsimulation envelope. Coordinates/signatures alone do not establish
  physical inspection. Mutable input records are cloned before verification.

Placement reuses `PlacementTransform2D.Align`, existing analytic punch placement,
holder material sets and **all** SPLINE/placement error bands. Listed holder
segments have their own origin/sign/error in the same bend frame; no infinite
extrusion or copied punch length is assumed. Unsupported taper/axial profile
changes require another contract and currently return UNKNOWN. This phase adds
no new file setting, company evidence or UI handler; consumers can supply the
typed records to `ToolAxialSetupEvidence.Read`.

## Collision proof and numerical policy

`AxialCollisionEngine` intersects each certified sheet interval with the finite
tool segments. Proven axial separation can give a lower clearance bound.
Otherwise it checks actual slice material for a robust interior witness and
calls the unchanged Phase 3C `CollisionEngine2D` on the enclosing material for
the **whole interval**. An intersection with an enclosing set is only UNKNOWN;
it is never mistaken for an intersection of true material. Any unsupported or
unseparated interval prevents CLEAR.

XY separation at the same Z alone is not a 3D distance certificate: nearer
material may lie just outside a tool cap. The solver includes the cap neighborhoods
out to the combined axial error plus twice the collision tolerance, and includes
the lower distance to excluded axial material in its global minimum. As a result
`CertifiedClearanceLowerMm` can be small (often about twice the tolerance); it is
a conservative **lower bound**, not an exact minimum clearance measurement.
Proven empty overlap can be CLEAR with no numeric distance instead of an invented
infinite value. All certificates are static `FinalModelObserved` geometry only.

| Quantity | Policy |
| --- | --- |
| Collision/containment/boundary bands | Existing Phase 3C analytic/interval policy, default tolerance `1e-5 mm`, including holder SPLINE deviations; no replacement solver. |
| Native solid and section source allowance | `1e-5 mm`, retained in XY and axial cap guards. |
| Native joint/event identity check | `1e-10 mm`; no loose tolerance closure or joining of distinct features. |
| Unresolved native slab | Less than `4e-5 mm` returns UNKNOWN. |
| Controlled analytic source allowance | `1e-9 mm`, plus outward arithmetic/transform and interval-motion bounds. |
| Tool axial error | Explicit reviewed value, finite, `0..0.1 mm`, plus transform roundoff. |
| Resource limits | Pair cell/time/kernel limits and batch time/query limits; exhaustion/cancellation gives UNKNOWN, never retained partial CLEAR. |

Witnesses separately expose the cross-section interior margin and the axial
occupancy/event margin. They do not claim a 3D interior-ball radius for moving
or curved surfaces. Stale results expose neither a usable clearance nor witness.
Boundary contact and error-band-only overlap remain UNKNOWN.

## Async entry and compatibility

1. On the existing STA, capture the Part once with
   `BendPlacementNativeCapture.Copy(item)`; load/freeze the existing library DTOs.
2. Build `CollisionBatchRequest(model, library, optionalEnvelopes, optionalSetups)`.
   Without supplied envelopes, the worker compiles the native proof. Without
   reviewed finite setup evidence, it keeps each pair UNKNOWN with reasons.
3. Await `CollisionBatchWorker.RunAsync(request, options, token, progress)` and
   consume its immutable result. The caller owns/awaits the task. Geometry
   preparation and kernel execution run on managed workers; never pass a native
   Part/body into this API. `RunSynchronously` exists for compatibility/testing.
4. `Published` changes atomically only after all slots finish. A newer run
   supersedes earlier results; disposal/cancellation/freshness invalidates held
   certificates. No worker updates a WinForms control.

`PrepareForAsync` reuses existing candidate preparation while skipping the old
constructor-time collision call. All previous default synchronous APIs retain
their behavior. Malformed preparation becomes an UNKNOWN diagnostic while
preserving slots. Work is dispatched tool-round-robin across bends, with bounded
1..4 concurrency and serialized monotone progress; dispatch order differs from
parallel completion order. Public output stays bend-major, 18 fixed IDs per bend.
Progress-listener failures are isolated and reported. Job clocks stop when a
batch finishes; source/review freshness remains live afterward.

The current WinForms/event flow is deliberately unchanged, as required by #14.
It still uses its existing compatibility path; this phase supplies the tested
worker API for a later caller integration. It does not claim UI background
adoption or an existing UI speedup.

Every row keeps independent Punch and Holder results. Before/during/after states,
manufacturing status and a winner remain unverified, even for a static CLEAR.
No V-die check is performed. See [observed validation](Validation-Phase3D.md)
for supported fixtures, all 18 actual-tool blockers and native limitations.
