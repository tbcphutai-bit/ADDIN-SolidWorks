# Qualified KEEP/CUT rules

## Inputs and policy

Physical profile strips, original t/R and axis come from native API/B-rep evidence. Construction lines are excluded. Web selection uses the actual pair's placement and paired native skins; extrema only bound cutters/closure, never identify a Web or choose a manufacturing rule. Unsupported multiple solids, non-rigid/mirrored placement, non-orthogonal axes, ambiguous Webs or formed-Bend evidence are rejected as `UNVERIFIED_RULE`.

The shorter actual Frame covers according to the user's convention. Base-extrusion stock length and A/B order do not choose the winner. Equal actual lengths require a consistent cover-side rule and are refused when ambiguous. Unequal heights may leave exposed Wall/Lip edges; qualified local axial-cap measurements report their area. Those probes do not measure every diagonal edge or visibility direction.

## Geometry

The common seam follows native Bend tangencies. Unequal widths preserve the geometric equal-leg bisector and move the near endpoint to the limiting real tangent; they do not stretch the diagonal between arbitrarily chosen corners. The general folded branch retains the full formed chain, including near returns and inward/reflex suffixes, and uses separately constructed A/B stepped removal regions. Its far shoulder offset must preserve the normal fit gap, not just the straight Web line.

The short Frame's covering footprint is kept. The losing Frame receives its complementary stepped Web/Wall/Return cut; separate qualified Bend-cap/clearance operations execute in order. Each temporary/native result must retain one valid connected solid, required stock, zero unintended interference and the exact reviewed target geometry. A Boolean non-manifold failure is a failure; no guessed cutter perturbation is retried to obtain PASS.

Only the `GeneralFoldFootprint` branch has the added-stock removed-versus-sweep guard. Its native interior sections at 35% and 65% must agree; planned removal outside that independently measured sweep is rejected. This guard does not prove every longitudinal variation or classify an addition captured identically by both samples. The legacy R55 plain/outward-U route is preserved and has no claim of this generalized guard or universal arbitrary added-stock protection. Existing added material is not recreated. Finished-reference local Wall-plane cells remain a separate unqualified production extension.

## Independent 0.10 mm meanings

`AssemblyFitGap = 0.10 mm` is assembly clearance measured normal to the mating separator. `SheetCutOverrun = 0.10 mm` belongs to a verified single-sheet Blind cut's depth `t + 0.10 mm`. Their numeric values agree but they are separate policies. Formed-profile envelope cuts and compound/two-direction reference Cuts must not be replaced mechanically by single-layer depth.

All 20 qualified generated source15 Cuts across 5 pairs report `NormalCut=false`, including operations named `NORMAL_BEND_CAP`/`BEND_NORMAL_RELIEF`. They use independently validated explicit Boolean relief construction. Their actual flags are preserved in the inventory; they are not claimed to reproduce the authored feature command history. The single-sheet NormalCut=true Wall-sliver prototype remains outside delivered production source pending complete native material/Bend qualification.

## Native parametric construction

All four Sketches attach to existing Part Face2 supports. Master/loser contours share geometric relations; native support edges/faces provide Coincident, Parallel, Perpendicular and equal-leg relations. Closure and gap guides share only two manufacturing dimensions. Relief Sketches have zero driving dimensions. Every final Sketch requires native status 3, zero dangling/overdefining relations, preserved seed contour and exact original Face support. No new Plane, Convert or FIX is used.
