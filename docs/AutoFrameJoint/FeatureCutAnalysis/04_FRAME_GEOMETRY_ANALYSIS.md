# Frame Geometry Analysis

## 1618a profile observations

`GEOMETRY_VERIFIED (supplied-derived STEP inventory only)`: the supplied STEP-derived table lists six valid B-rep solids with volumes and axis-aligned bounds. Profile-like section bounds include 65.0 × 17.0 mm and 77.0 × 17.0 mm on long Z-spanning solids; long X-spanning solids include 90.0 × 28.6 mm and 80.0 × 27.0 mm bounds. Values are preserved in `FEATURE_CUT_INVENTORY.json`, not independently recomputed here. Solid indices are not mapped to component occurrences.

`INFERRED (reference-image only)`: the section chart labels fr1 as asymmetric with an inclined lip, fr2 as a simpler U-like section, fr3 as deeper with multiple returns, and fr4 as another U-like return. Displayed approximate labels: fr1 95.7 × 17.6 mm, fr2 65 × 17 mm, fr3 90 × 28.6 mm, fr4 77 × 17 mm. These are not native sketch dimensions. Thickness, Bend R, assembly-space lip direction, and axis transforms remain unverified.

## Joint cases and regions

Prior projection images label `fr1 × fr2` at lower-left and `fr3 × fr4` at upper-right and show different projected boundary patterns, including diagonal lines. They do not establish the underlying operation or removed material. Another comparison image shows 1618a against 3615a T1/T2 at displayed section heights 4/18/34 mm; exact section/occurrence mapping is not verified.

For both members in every case, Web, Wall, Lip, and Bend R action is `NOT_AVAILABLE`. No region is labeled KEEP or CUT from B-rep evidence. Preserving an outer projecting lip and considering an inward-return trim remains `INFERRED`, not a production rule.
