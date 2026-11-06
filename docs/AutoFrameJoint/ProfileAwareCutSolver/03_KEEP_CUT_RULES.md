# Parameterized KEEP / CUT rules

## Manufacturing policy and frame roles

The user explicitly confirmed: shorter actual Frame covers; assembly gap g=0.1 mm. Length comes from exact body extrema along the native profile axis, independently of base extrusion stock length. A/B order does not determine priority. Native examples with a different cover remain historical evidence. KCC 26003 defines cover/thickness-edge visibility and short-direction panel priority in its stated panel scope; it is not used to invent a universal Frame rule.

## Common geometry

Let s point toward the selected end of the losing member and q toward the selected end of the cover member. z is depth from their coplanar outer Web skin. W/t/R come from measured section geometry/API. The Web is the unique line between parallel same-side perpendicular Walls; optional far Return is found by connectivity and direction. Native filenames, Cut names, exact pose and cut sketch coordinates never enter production rule selection.

Far Bend tangencies are sf=Wcover-tcover-Rcover and qf=Wloser-tloser-Rloser. Cover near tangent sn=tcover+Rcover. Start with a 45° compatible seam qn=qf-(sf-sn), constraining qn to loser inner nearWall q=tloser when it would enter the near Bend. The resulting seam slope is (sf-sn)/(qf-qn), which is not a fixed 45° plane for all sections. Loser Web shifts by g*sqrt(1+slope²) along s to create a normal Web gap g.

| Region | Cover KEEP/CUT | Loser KEEP/CUT |
|---|---|---|
| Web | Complementary tangent-based seam | Remove beyond offset seam; retain interior Web |
| Near Wall | Retain covering wall to opposite inner Wall datum | End at s=-g |
| Far Wall | Retain covering outer end at q=Wloser | End at s=Wcover-tcover-g |
| Outward far Return | Keep outer contour through the mating envelope | Keep Return to cover's actual outer tip; transition at q=Wloser+g |
| Bend | NormalCut cap derived from inner farWall plane and t+R depth | Near/far tangent guards prevent residual point contact after Web offset |

Cut tools close at finite exact body extrema plus margin; those extrema bound the tool, they do not infer the manufacturing cut. Separate plans may contain multiple sequential operations on each member. The full-profile envelope prism spans the formed section; it is a temporary Boolean mask, not a claim that a single-sheet manufacturing feature should use the full section height as its depth.

## Required KEEP validation

Independently reconstruct an ideal local U/Return stock from measured t/R, Walls and Return. Apply the generated plan to that stock within a small joint band. Any ideal required KEEP volume absent from the actual result causes REQUIRED_KEEP_MISSING. This detects insufficient stock, an earlier incompatible end trim and holes in the coverage band. Cutting cannot restore missing stock. The opposite end is protected by the selected-end removal guard.

## Unsupported families

Near/inward Return, multi-step/double Lip, inclined Wall, unequal t/R within a pair, mismatched outer heights, non-90° joints, offset skins, mirrored placements, imported-only/flat/unresolved/multiple-body Parts and equal lengths need more verified rules. They return UNVERIFIED_RULE without fallback cutting. Rejection is not a geometry PASS. See JOINT_RULES.json for equations, preconditions and numerical tolerances.
