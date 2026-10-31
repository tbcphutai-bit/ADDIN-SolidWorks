# Geometric rules and evidence

## Qualified construction

The implemented rule is one bounded orthogonal U-profile family, with primary Web, primary Walls and a certified outward folded suffix. It does not select a different frame family merely because a U section has more folds. Current native profiles include fr01 Web 100 mm plus a 23 mm outward Lip; fr02 primary Web 55 mm and six ordered straight strips; fr03/fr04 primary Web 70 mm. These dimensions come from the current API baseline, rather than the older archived fr01 70 mm study.

1. Select the corresponding primary Web from native ordered profile and physical skin evidence. Do not infer it from a filename, longest segment or bounding box.
2. The short Frame is the cover. For unequal primary Web widths, the two native tangent pairs define a common 45-degree Web seam with shoulders before/after the shared diagonal, preserving its direction rather than stretching it across unrelated complete widths. The equal-width branch preserves its older inner-wall near-end clamp; its diagonal can differ slightly from exactly 45 degrees. Four-corner B-rep validation does not change that angle convention into an exact bisector claim.
3. Allocate each primary Wall, outward shelf, secondary Wall, terminal Lip and connecting Bend to an explicit KEEP/CUT boundary. A monotone outward covering suffix shares the opposing outer far-wall longitudinal cutoff only after the complete native strip/skin/Bend certificate proves separation. This regional cutoff does not replace the whole section by an axial butt cut.
4. A losing far Wall uses the covering member's actual inner far-wall skin minus `AssemblyFitGap`. An outward losing Lip uses its own certified piecewise continuation. All formed-stock/Bend material is checked against the reviewed target, not only against the straight sketch chain.
5. Face-attached native master/loser Sketches use direct geometric relations, inherited end points and shared gap/closure roles. Local Bend cap operations remain ordered after the main cuts. No new Plane, Convert Entities or per-vertex coordinate dimension ladder is part of this route.
6. A nonmatching cover height is allowed under the user's exposed-edge policy. The measured axial Wall/Lip cap area is reported; concealment of diagonal Web edges and all view directions is not asserted.

The four-corner results are `GEOMETRY_VERIFIED` for their actual native source profiles. Applying the convention to additional profiles is `INFERRED` until complete native skin/Bend, required-KEEP, connected Body and exact-result checks pass. Unsupported profiles must stop with an unverified rule instead of choosing an arbitrary cutter.

## Two independent 0.10 mm values

- `AssemblyFitGap = 0.10 mm` separates the final mating bodies. Each final corner independently measures that minimum gap, within the harness tolerance of 0.001 mm, and zero overlap.
- `SheetCutOverrun = 0.10 mm` is a manufacturing overrun for a suitable single sheet layer. It is not an Assembly gap and is not blindly added to every complete formed-profile cutter.

The actual generated native depths in this run are **2.7, 2.8, 40.2, 42.2, 44.7, 60.2 mm**. End conditions: `{"Blind": 16}`. Native `NormalCut=true` count: **0/16**. The actual Blind main cuts use a formed-profile envelope; separate low-depth Bend relief/cap cuts cover their local regions. The identifier `NORMAL_BEND_CAP` names a geometric operation and does not mean that the native `NormalCut` checkbox is enabled. Consult the inventory for each actual flag/depth rather than replacing these values with a universal 2.60 mm cut.

## W23 reference

The current requested W23 inputs match 9 prior native inputs by SHA256. Prior metadata inventories expose Face2-supported Sketches and stepped diagonal/shoulder traces; they do not prove before/after removed B-rep for W23. The special 59.130328-degree projected trace also refers to third component ft1 and is not a generic two-member solution. W23 native Apply and Bend-clearance qualification remain outside the four-corner 2354 result.
