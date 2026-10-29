# Per-strip angled-contact method — research design

Status: **INFERRED_RULE_DESIGN**, derived from native 4458 evidence. Not implemented or native-qualified. The 2,05° reference is a measurement, never a runtime constant.

## Why the current solver needs a different geometric representation

The current main source (`B56D83128310C0A3AF0549507B6E371FEC28C160888147A78EDB9CA73864A095`) routes RuleLibrary.Create into ParametricRules.Generate. Initial Section classification can reject an inclined primary Wall or Return before the later general folded graph path executes. Existing inward flags therefore do not establish support for angled inward folds.

The existing per-fold reader already measures a strip axis, transverse direction, finite skin pair and material normal. Its contact algebra can express affine boundaries, and its cylinder/arc routines are not inherently limited to 90° bends. However CrossingBand, same-side matching, constant TerminalAnchor and some sketch-guide bindings retain stronger orientation assumptions. Generalization must preserve the measured graph before assigning Web/Wall/Lip labels.

The generic `SelectedEnd` validation uses one origin/outward halfspace for all removed material. A measured local sloped boundary may vary across a sheet. That observation does **not** prove the earlier 2222 failure was a false positive; its removed region and selected-end authority remain unverified. Do not disable the protection. Replace or supplement its common limit only after local KEEP/CUT authorities are qualified.

## Local geometry and allowances

For each native planar sheet strip use a right-handed local basis and its finite domain:

```
P(s,v,w) = B + s*a + v*u + w*n
0 <= w <= t
```

Here `a` is the actual member axis, `u` the strip direction and `n` the material-thickness direction. Determine material side from paired B-rep skins, not image color or an assumed sketch winding.

For a unit mating plane normal `M`, plane support `d` and signed fit-gap side `sigma`:

```
M.P = d + sigma*AssemblyFitGap
s(v,w) = (d + sigma*g - M.B - v*(M.u) - w*(M.n)) / (M.a)
```

The sign must come from the selected KEEP side/short-Frame cover policy. Never choose it by A/B order. When `M.a` is near zero this plane does not define a safe end cutoff; require another finite contact authority or refuse the rule.

With more than one finite mating skin, split the strip into active contact intervals and construct the appropriate piecewise envelope. Use actual trimmed-face/stock bounds to stop an infinite supporting plane from cutting unrelated material. A fold turning inward can reverse the local basis and change which mate bounds it; `own.Side == mate.Side` is insufficient as a general matching rule.

**Normal Cut constraint:** a 2D contour extruded along the own sheet normal cannot freely encode a boundary that varies through `w`. If `M.n` is nonzero, evaluate the required thickness envelope and KEEP/clearance feasibility explicitly. Refuse unsupported cases instead of asserting a slanted tool is a Normal Cut. The two selected reference cap Sketches have `M.n` approximately zero, so their measured sloped contours are compatible with normal extrusion on those particular support faces. This does not qualify every other fold or Bend.

The allowances remain separate:

- `AssemblyFitGap = 0,10 mm` along the mating plane normal. Its equivalent axial shift is `g / abs(M.a)` when that parameterization is valid: approximately **0,100064041895 mm** for the measured 2,05° case.
- `SheetCutOverrun = 0,10 mm` added to the API thickness along the own material normal. With t=2 mm, single-layer Blind depth is **2,10 mm**.
- The reference boundary changes by **5,154411621 mm across 144 mm**. A single constant axial cutoff cannot represent it. Keep the affine boundary or piecewise finite-contact envelope.

## Cutter, native Sketch and Bend handling

Produce separate ordered operations for the actual sheet cells. Cut direction comes from the paired skins/material-side test. Closed cutter footprints can represent a deterministic removed side without copying the historical FlipSide setting. Preserve historical open-line/FlipSide records as evidence, rather than treating them as the only possible writer recipe.

Each native Sketch2D belongs on the existing selected Face2. Resolve finite native edge/vertex or owned-guide authority, then add Coincident and other measured geometric relations. Preserve the 2,05° guide relationship rather than snapping it to global horizontal/vertical. Minimize dimensions; the technology gap may have a dedicated controlling parameter/guide, but do not dimension every generated endpoint. The historical Sketches include Distance relations, so they are not zero-dimension examples. An unsupported reference-binding recipe must remain UNVERIFIED_RULE. No Convert Entities or reference-plane fallback is proposed.

Predict Bend removal from adjacent native R/R+t cylinders, tangent directions and actual angular intervals. Treat NormalCut as an explicit native feature requirement. Blind t+0,10 alone is not proof that an inward angled Bend was cleared. Avoid zero-volume/tangent-only Boolean contacts by qualifying positive-volume removal and connected KEEP bodies, without silently changing the 0,10 mm manufacturing allowances.

## Required qualification before implementation admission

Keep the existing architecture:

```
A/B -> measured Profile Graph -> qualified Rule -> ordered Cut Plan A/B
    -> temporary Boolean -> local/global Validation -> existing Ghost Preview
```

For each local operation verify:

1. Removed material stays in its approved finite selected-corner cell; protected core and opposite-end B-rep witnesses retain material.
2. It removes intended interference/excess, preserves required Web/Wall/Lip coverage, and reports any allowed uncovered edge.
3. Both KEEP bodies remain valid connected solids; normal fit gap and complete pair overlap are independently measured.
4. Native Feature Cut before/after matches the planned temporary result, including Bend R, on disposable copies. Read back NormalCut, Blind depth, direction and constraints.
5. One identical reviewed plan drives Preview and native Apply; preserve the existing A/B callback, command, session and cleanup mechanisms.

Reference read-only extraction and mathematical checks are complete. New per-layer cutter B-reps, native Cut replay and four-corner tests have **not** run. They must be qualified before claiming this rule solves 4458, 2222 or general inward/angled profiles.
