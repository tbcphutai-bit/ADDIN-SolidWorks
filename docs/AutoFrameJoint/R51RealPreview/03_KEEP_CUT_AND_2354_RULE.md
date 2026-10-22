# KEEP/CUT and the 2354 rule extension

## Measured input

The debug pair is assembly 2354-02-lc-B11: A=fr04, B=fr03. Current stock has its corner Cuts suppressed. API/geometry measurements give A span 1500 mm and B span 3200 mm. Both are plain formed U sections with Web width 70 mm, thickness 2.50 mm and inner Bend R 0.10 mm; no Lip/Return is present in this pair.

After correcting the selected end using actual body span, the corresponding near Walls are **40/40 mm** and the far Walls are **60 mm covering / 42 mm losing**. An earlier question about a 40/42 mm covering conflict used the opposite end pairing. That question is superseded by the measured selected-end frame here; no additional drawing was needed to resolve that pairing.

The material occupies the expected Web planes 0/t and Wall planes 0/t/W−t/W. Both inner/outer base Bend cylinders match the expected centers and radii. This is checked on the B-rep; a Sketch thickness-side Boolean is not sufficient.

## Common coordinates and manufacturing policy

The user confirmed that the shorter actual Frame covers. The solver chooses that member using exact longitudinal extrema, then verifies its profile and covering geometry. It does not infer all trims from length. Let s point outward along the losing Frame, q outward along the covering Frame, and z into the folded section from its common outer Web skin.

The original verified U family is retained for equal contour levels. The extension `ORTHOGONAL_U_TALLER_COVER` requires:

- orthogonal axes, rigid nonmirrored placements, coplanar Web skins and compatible material side;
- one supported formed U/body per component, equal measured t/R;
- no near/inward Return;
- for unequal heights, **both sections must have no far Return**, and the covering near/far Wall heights must reach the corresponding losing Wall heights;
- sufficient actual KEEP stock in the joint band and no removal beyond the selected end.

A lower covering Wall or unequal Return levels returns `UNVERIFIED_RULE`. The height comparison is not a general treatment of inclined Walls, step profiles or arbitrary T joints.

## Independent plans

| Region | Shorter covering member A | Losing member B |
| --- | --- | --- |
| Web | Retain side of the seam joining verified Bend/inner-Wall datums | Remove beyond its own offset seam; normal separation g=0.10 mm |
| Near Wall | Retain the covering face to the opposing inner-Wall datum | Cut to a plane g behind the covering outer skin |
| Far Wall | Keep the actual outside covering end; height 60 mm is retained | End at the covering inner-Wall plane minus g; height is 42 mm |
| Lip/Return | Not present in 2354; existing equal-level U/Return family retained | Unequal Return levels are refused |
| Bend | Inner far-Wall/tangent-derived NormalCut cap | Near/far clearance guards prevent a residual tangent contact |
| Excess axial stock | Finite cutter extends to exact stock extrema plus envelope margin | Independently bounded from its own stock extrema |

The generated tools are volumetric prisms, not `A∩B` material. Intersections used during execution measure removed volume; the subtraction uses the independently generated tool itself. Multiple operations execute in order on disposable copies.

The seam is derived from thickness, Bend tangencies, inner Wall planes and section widths. It is not a universal 45° plane and is not read from a native Cut polygon or a fixture coordinate table. Frame names, profile signatures and assembly coordinates do not select a rule.

## 2354 result

| Quantity | A fr04 | B fr03 |
| --- | ---: | ---: |
| Before, mm³ | 614404.312809 | 1166729.200659 |
| After, mm³ | 542650.915128 | 1098063.506654 |
| Removed, mm³ | 71753.397681 | 68665.694004 |
| Kept bodies | 1 | 1 |
| Check2 / Check3 faults | 0 / 0 | 0 / 0 |

Minimum gap is approximately **0.100000 mm**; interference volume is **0**. Required KEEP and selected-end checks pass. The public Ghost method displays the two modified Frames in both modes and Hide succeeds. The display remains offset in world Z by the existing +120 mm convention; no original component is moved.

## Historical Cuts and differences

Three suppressed native Cuts were enabled only on new Read-Only study copies. The short member has an open straight stock-end Cut followed by an open polyline Cut; the long member has a closed contour Cut. NormalCut is enabled on all three. They provide evidence for end-stock trimming, distinct Web/Wall boundary portions and formed Bend handling.

The generated policy is **not an exact replay** of those historical Cuts. Measured symmetric differences are approximately **255.441109 mm³ for A** and **265.359003 mm³ for B**. The generated plan gives the shorter member the near-Wall cover and explicitly applies the fixed clearance/Bend guards. Historical geometry makes a different local near-corner choice. An exact decomposition of every difference into policy and Bend contributions has not been established.

This comparison is kept visible for Code Owner review. Native feature parameters are `API_VERIFIED`; resulting bodies and differences are `GEOMETRY_VERIFIED`; interpretation of manufacturing intent beyond the confirmed policy remains `INFERRED`. No drawing approval or native-identical machining result is claimed.

## Native before/after limits

All three enabled native after bodies have Check2=0 and no measured material addition beyond their respective before stock. Only the short member's first stock-end Cut passes the strict before/after volume-difference tolerance. The other two have residuals of about **0.455335 mm³** and **6.152731 mm³** between volume subtraction and removed-body integration. The cause is unresolved; they remain unverified under that strict volume criterion. The tolerance was not relaxed to turn them into passes.

The generated R51 plan has its own strict conservation and validity checks; its successful result does not erase those native-study limits.
