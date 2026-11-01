# Joint rules and geometry

## User-confirmed conventions

- The shorter actual Frame has priority to cover the joint.
- `AssemblyFitGap = 0.10 mm` is the intended assembly clearance.
- `SheetCutOverrun = 0.10 mm` is extra Cut depth for a qualified one-layer sheet cut. It is not the same operation as the assembly clearance.
- When the shorter Frame cannot conceal the complete cut face, a valid butt solution may be produced with the exposed area reported. This permission does not establish full coverage.

## Existing U miter

The existing qualified U/Taller-U miter remains the route for its previously accepted sections and placements. The new fallback does not replace those shapes or reproduce their historic Feature Tree operations. The native writer for these old rules has not yet been rewritten to satisfy the new face-attached/direct-relation requirement.

## `ORTHOGONAL_FOLDED_SHORT_COVER_BUTT`

This is a distinct **axial butt** strategy. It supports a complete ordered, nonbranched, linear base-profile chain, including multiple bends, inclined strips and inward/outward returns. Every strip is retained in the graph. No longest-line/bounding-box heuristic defines the Web or Cut.

The fallback is selected for complex/no-unique-Web chains and explicit placements outside the old U policy, including qualifying near returns, a lower short covering member and unequal thickness/radius pairs. Each member must satisfy its own native planar-skin and bend checks. Previously accepted plain-U/Taller-U placements retain their old miter route.

### Two independent cut planes

1. Establish each native extrusion axis and actual axial span. Determine the outgoing selected end from the physical midpoint and complete profile vertices. Ambiguous T/interior selections remain refusals.
2. Require perpendicular Frame axes.
3. The long-member cutoff is the short member's actual minimum transverse support along the long outgoing axis, minus the assembly gap.
4. The short-member cutoff is the long member's actual maximum transverse support along the short outgoing axis.
5. Support must be witnessed by trimmed native topology at the true `Body2.GetExtremePoint` result. A matching planar support Face or a native vertex/edge boundary can qualify; this does not infer support from a bounding box or an infinite plane alone. The tested inclined 1618 case exercised planar and vertex support; the edge-only branch remains untested. Each cutoff must lie on the selected half of its member.
6. Construct separate finite axial Cut prisms for A and B. Transverse envelopes bound finite tool containment; the actual cutoff support positions come from verified native B-rep extreme/boundary witnesses. These geometric positions implement the stated BUTT policy.

This construction does not require a shared Web plane. When a unique Web cannot be established, region labels stay `UNKNOWN_GRAPH`; ordered topology does not invent Web/Wall/Lip semantics.

### Qualification and required KEEP

- Each base-profile line must match actual paired longitudinal planar skins at one nominal support and the opposite skin at `+t` or `-t`.
- Each turning node must match adjacent coaxial native `R` and `R+t` cylindrical skins. Collinear split lines do not invent bends.
- Native interior slice caps are copied to sheets and extruded into the joint band. Two sampled stocks must agree by symmetric difference and include the required skins/bends. Hole loops, incomplete stock and inconsistent samples are refused.
- Expected KEEP from that stock is compared with actual KEEP. A zero-overlap result alone is insufficient.
- Each Frame must retain one valid solid; final native overlap and minimum gap are checked.

The 2354 fr02 × fr01 positive case passed these geometry checks. A 1618 fr3 × fr4 stock-copy case also passed after pre-existing Cuts were suppressed only on disposable copies; its complete chains contain six and four strips. The already-trimmed 1618 input remained refused. These results do not qualify every profile, joint angle, reused Part or assembly configuration.

## Exposed long Cut face

The diagnostic starts from actual long-member Cut cap faces. It moves a copied cap sheet across the gap to `t_short/2` inside the short member and extrudes a 0.01 mm probe slab. Covered area is intersection volume divided by probe thickness. Exposed area is actual cap area minus covered probe area.

For the tested 2354 pair: total 364.602875 mm²; exposed 29.977776 mm². For the 1618 fr3/fr4 stock-copy pair: total 330.478760 mm²; exposed 127.444101 mm². For unequal-width fr1/fr2 stock copies: total 240.645027 mm²; exposed 23.117370 mm². These measure coverage at the probe section, not all-angle visual occlusion. A failed diagnostic returns `UNVERIFIED` and null areas, not zero. Positive exposure does not reject a butt permitted by the user.

## Native representation

For the qualified new butt rule, the copy-tested writer creates a Sketch on an actual selected end Face, reconstructs its exact end-section lines/arcs, adds direct native vertex/tangent relations, then creates a native Cut. The 2354 writer used Blind depths 145.0 mm and 223.1 mm; the 1618 stock-copy writer used 69.85 mm and 402.25 mm. These operations trim complete formed profiles axially. Applying `t + 0.10 mm` to those depths would be incorrect.

Cross-Frame Up To Surface/Offset From Surface remains a separate experiment. Although one parameter-change trial passed, the owned Undo qualification failed because recording did not finalize. The adapter is not qualified or integrated into the production candidate.
