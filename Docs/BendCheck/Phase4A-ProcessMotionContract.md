# Phase 4A: executable process geometry and continuous motion

Baseline: PR #19, `949dd92f6cf7dd2fd1f4e8e9333920457900748f`.
Issue: [#21](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/21).
New code is isolated in `Commands/BendProcess/`. No UI integration is made.

## What is actually evaluated

`ProcessCollisionEngine` tests one explicit motion segment against either Punch
or Upper Holder. It calls the existing Phase 3D `AxialCollisionEngine`, which
calls the unchanged Phase 3C analytic LINE/ARC and bounded-NURBS solver.
No V die, machine operation, order search, tool ranking or manufacturing verdict
is implemented. `ManufacturingStatus` remains `CHUA_XAC_MINH`; winner is null.

Five distinct process phases are modeled: `ApproachLoad`, `PreBend`,
`DuringBend`, `FormingEnd`, `RetractUnload`. Multiple explicitly joined forming
stages are supported. Missing phases remain UNKNOWN, even when another segment
has a certified CLEAR. A witnessed interference can still be reported without
claiming the uninspected phases passed.

`FinalModelObserved` is a separate geometry role. It is rejected as the source
of a process part. `ProcessBatchRequest.ObservedFinalGeometry` retains native
coverage as provenance only and never constructs an unfolded blank, intermediate
shape, feed/retract trajectory or bend sequence from the final Part. An explicit
external process description is required to declare a source in-process.

## Source, frame, setup and freshness contract

`BendProcessPlan` freezes the supplied managed segments, components and contact
records. Its payload hash binds:

- Part snapshot ID, update stamp, configuration, body/bend ID, full work-frame
  registration and observed geometry source hash (`EnvelopeBinding`).
- Independent process-source hash, all panel source geometry hashes and roles,
  finite axial domains, fidelity, laws and uncertainty.
- Every segment's constraint-source hash, phase, Punch and Holder source
  profiles, measured finite segments and errors, setup hashes and pose laws.
- The entire intended-contact definition and its evidence hash.

`State(segment,u)` publishes immutable sheet envelopes, finite tool occupancies,
source roles, laws/errors, constraint/setup hashes and contact records at a
specific normalized progression. It is an observation, not an interval proof.

`Controlled` always means **simulation only**. Coordinates, a hash string or a
software fixture do not verify an actual tool/machine setup. `Reviewed` requires
the existing trusted external signature/artifact verifier for purpose
`BendProcessPlan` and a separately verified physical Punch–Holder pair.
Non-simulation poses must come from source-bound `ToolAxialSetupEvidence`, not
an arbitrary `AxialToolOccupancy`. `ProcessToolPose.FromEvidence` retains that
receipt. Simulation sources remain simulation-only after external review.

Adjacent panel sources must match and their absolute poses must join; the same
applies independently to each tool, including **all finite axial segments**.
Changing tool length/gaps without a modeled transition is UNKNOWN. Phase order
must not reverse. Equal rotation matrices are required; a <=1e-9 mm arithmetic
join residual is covered by the >=1e-8 mm guard on both neighboring segments.
Larger unmodeled registration jumps are rejected.

Model rebuild/configuration invalidation, source/library/review leases,
process invalidation, supersession and worker disposal invalidate held results.
An expired/stale result exposes UNKNOWN and no usable collision witness. The
caller must refresh the existing source/review verifiers when files change and
invalidate the process lease when its independent source changes; this phase
adds no UI event handler or automatic process-file watcher.

## Whole-interval proof, not angle sampling

The admitted explicit law is rotation about registered bend Z plus affine XY
and axial translation, with parameter `u` in [0,1]:

```
theta(u) = theta0 + u * (theta1 - theta0)
p(u) = pivot + R(theta(u)) * (pSource - pivot) + translation(u)
z(u) = zSource + axialTranslation(u)
```

For a parameter interval `[a,b]`, let `m=(a+b)/2`, `h=(b-a)/2` and let `r` bound
the distance of **every** source boundary point from the supplied pivot. LINE
endpoints suffice for a LINE; ARC center distance plus its radius bounds the
complete ARC. Source holes and disconnected components are included.

The entire boundary displacement from the midpoint pose is enclosed by a disk:

```
reach <= r * (abs(theta1-theta0)*h + angularError)
         + length(translation1-translation0)*h + positionError
         + numericalGuard
```

The rotation bound follows from `|R(alpha)p-R(beta)p| <= |p|*|alpha-beta|`.
Arithmetic uses the existing outward interval operations and a declared
`1e-8 + 1e-12 * coordinate/radius scale` mm transform guard. All source contour
uncertainty, including NURBS deviation bands, is **added**, never stripped.
The midpoint outline plus every boundary band is an enclosing material set,
including contracted holes. Large bounds may be inconclusive; adaptive
subdivision tightens them, not an unsupported assumption about the motion.

`MotionSolid` wraps a complete Phase 3D/3D.1 sheet envelope, preserves its source
partition, and encloses the complete preimage of each axial interval, expanded
by axial pose uncertainty. Thus the proof covers both parameter and axial
continuity. Tools retain the independently supplied finite segments; axial
translation sweeps each segment separately, preserving gaps except where the
declared actual movement can cover them. No infinite extrusion is assumed.

For each adaptive cell:

1. Exact source states may establish a robust unwanted intersection witness,
   including positive XY and finite axial occupancy margins.
2. Complete sheet-motion and tool-motion enclosures are tested by the existing
   axial/kernel solver. Only a **CLEAR** of these supersets certifies this cell.
3. An intersection of the supersets is **not** a collision witness. Inconclusive
   cells are subdivided, or retained as UNKNOWN at a resource/depth limit.

Endpoints/midpoints serve only as witness opportunities. The initial endpoint,
midpoint and final state can all be clear while a later interior witness proves
collision. If all cells are certified separated outside approved contact, the
segment is CLEAR. Any uncovered interval means UNKNOWN. A collision witness
is source/parameter-bound and is never taken from `Sweep()`.

Constant component/tool pairs are cached within a segment; their checked
immutable geometry and freshness guards are retained. An unresolved stationary
pair cannot be made more certain by repeating it at more angles.

Sheet axial pose uncertainty currently suppresses point-state collision
witnesses (nonuniform material may move along Z); complete swept bounds can
still certify separation. This restriction is conservative, not a zero-error
fallback. The Phase 3D kernel's existing source/cap policies remain in force.

## Bend zone and kinematics fidelity

The complete one-bend fixture has a fixed panel, a rotating panel and an
explicit bounded joint region, with declared pivot, panel assignments, angle
law and finite axial span. Both panel inner edges remain inside that declared
joint enclosure throughout rotation. Loading and unloading have explicit
translations; the pre/end phases have their own poses.

The fixture's bounded joint disk is a **geometric enclosure constraint**, not
an elastic/plastic forming simulation. `CertifiedEnclosureOnly` permits CLEAR
when the entire possible joint region is separated, but cannot supply a
material collision witness. An obstacle inside only this region is UNKNOWN.
`MissingBendZoneBound` makes the process UNKNOWN even when rigid panels look
clear. `ExactGeometricSurrogate` means the declared test geometry is defined,
not that it exactly predicts a real deforming sheet.

A second fixture explicitly supplies three panels, two joints and two alternate
two-step kinematic paths. Both end at the same geometry. One path interferes
at an intermediate step; the other has certified separation for its modeled
steps. These are comparisons of supplied paths; no best order is searched or
selected, and missing load/unload states stay UNKNOWN.

Native transverse-cylinder and trim geometry is reused through the Phase 3D.1
complete source compiler. The source does not grant a law or process-role
approval by itself. No Part rebuild is used to manufacture intermediate CAD.

## Intended surface contact: narrow and explicit

`PlanarFormingContact` currently proves **stationary, exact LINE-prism surface
contact** only. Its reviewed/simulation declaration is bound to one sheet part,
Punch setup, process phase, supporting plane, finite tangent range and finite
axial zone. It checks all vertices lie on opposite closed halfspaces, actual
supporting LINE segments overlap, all potential surface contact lies in the
zone, and the finite axial overlap is covered. Source physical uncertainty must
be zero and both pose laws identity for this proof.

This independent halfspace proof establishes nonpenetration of the complete
pair, rather than deleting intersections or masking a contact area. The
diagnostic is `EXPECTED_CONTACT`; CLEAR means separation **outside that
approved surface contact**, not a positive gap at the contact or a production
PASS. No volume penetration is allowed. Other parts are still checked.

No blanket Punch exemption exists. An out-of-zone/phase/stale contact, uncertain
source or general moving/rolling/ARC/NURBS contact cannot use this certificate.
It is tested normally: robust material overlap is COLLISION, ambiguous contact
is UNKNOWN. Holder contact is never exempted. Approach/retract phases cannot
use this forming-contact proof. General deforming contact needs additional
nonpenetration/registration evidence and remains unsupported here.

## Worker API and budgets

`ProcessBatchWorker.RunAsync` owns an awaitable task and uses 1..4 bounded managed
workers; no COM capture runs there. It publishes a complete immutable snapshot
in stable bend-major/company-ID order. Dispatch is tool-major across bends;
progress counts are serialized and monotone. Callback failure is diagnostic
only. Newer requests supersede old ones atomically.

Every bend retains all 18 IDs, both independent Punch/Holder results and every
required phase. Missing actual process descriptions yield ten explicit UNKNOWN
pair/phase entries per tool without inventing numeric work. The existing
Phase 3D static results are not overwritten.

Default pair policy: 1,024 adaptive intervals, depth 18, 10 s, with the existing
axial and 2D policy. Default batch: two workers, 30 s, 100,000 kernel calls.
Cancellation and limits return reason-coded UNKNOWN; tests of scheduling
determinism use a generous explicit 120 s job budget. Completed snapshots do
not expire merely because an execution clock keeps running.

## Phase 4B still requires

Reviewed actual blank/stage geometry and joint-zone bounds; real process laws,
machine travel and loading/withdrawal constraints; reviewed material roles,
physical seating/datums, tool axial positions and lengths; broader forming
contact proofs where needed; and validated search across the process sequence.
Only after all required conditions are covered could a later phase compare one
tool against the entire Part and select a feasible candidate. This API supplies
geometric predicates and evidence; it makes none of those production claims.
