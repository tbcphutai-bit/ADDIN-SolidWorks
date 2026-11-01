# Local coplanar sheet-contact rules

## Verified observations

The finished fr3 × fr4 pair contains separate finite coplanar Web and Wall cells. Their actual skin normals, thickness separation, finite boundary intervals and opposing trim faces were checked through API/B-rep. Feature IDs below are provenance only; the prototype selects geometry without hardcoded IDs or Feature names.

| Cell | Local patch | Target trim owner | Existing peer sheet / trim owner | Measured normal gap (mm) | Decision at fixed 0.10 mm policy |
|---:|---|---:|---|---:|---|
| 0 | UPPER_WALL | 2051 | 1585 / 1623 | 0.100000000 | NO_OP |
| 1 | LOWER_WALL | 2051 | 1529 / 1529 | 0.070710678 | Local rule required; not production-qualified |
| 2 | WEB | 1947 | 669 / 1391 | 0.070710678 | Local rule required; not production-qualified |

Cell 0 is already at the specified normal clearance. Cells 1 and 2 are about 0.070710678 mm apart; applying the user's 0.10 mm normal-gap policy requires about 0.029289322 mm additional normal removal. The source reference therefore does not universally demonstrate a 0.10 mm normal gap.

The authored Wall seam direction corresponds to actual mating boundaries on existing peer end stock. The upper Wall uses the existing MiteredFlange sheet and Cut boundary; the lower Wall mates an existing EdgeFlange boundary. This does not justify creating an EdgeFlange or imposing 45 degrees on every Wall of every U profile.

## Proposed generalization — INFERRED / native qualification pending

Repeat a local sheet-joint solve for each actual interacting finite coplanar skin pair. Pair opposite skins using actual API thickness, retain native trim loops and actual Bend adjacency, then derive the new seam from the measured peer boundary and fixed normal gap. Keep the short Frame as the covering member. A Face2-supported Sketch should use Coincident, Parallel and a shared perpendicular fit-gap guide; it must preserve the exact datum ownership.

The complete finite flat cell is the common volume of the two exact skin sweeps, clipped to the original Body. Holes remain part of the native trim loops. Full-sweep differences, Bend sectors and other end stock are separate protected or UNVERIFIED regions. A local prism never authorizes a whole-profile cut.

The strict local Wall probe rejected an endpoint before creating a Sketch. Trace03 then verified that endpoint 0 has an actual planar axial sheet boundary adjacent to Cut 2051, while endpoint 1 has an actual cylindrical Bend tangent owned by SketchBend 681. The planar boundary must not be replaced with an extended remote tangent. These are distinct datum kinds, not an exception that makes every edge a Bend.

## Bounded working stock rule

The separately tested full-depth stepped-miter route is limited to measured orthogonal constant stock. It checks native strips/Bends, complementary planar KEEP separation and independent interior-section stock. Only its GeneralFoldFootprint branch has the added-stock guard: two native interior sections at 35% and 65% must agree, and planned removal outside that measured sweep is rejected. Identical additions crossing both samples remain unqualified; the preserved legacy R55 route has no claimed generalized added-stock guard. This does not reproduce the finished reference's local Wall/added-end-stock seams merely by passing Boolean checks.

The final-source tests produced 20 native Cuts across five pairs. All 20 report NormalCut=false: they use formed-profile envelopes plus explicit analytic Bend relief. They are not qualified as native single-layer NormalCut operations or as equivalent flattened manufacturing contours.

`SheetCutOverrun = 0.10 mm` is extra depth for a qualified sheet cut. `AssemblyFitGap = 0.10 mm` is clearance between members. Their equal numeric value does not authorize using either as a universal cutter depth or offset.
