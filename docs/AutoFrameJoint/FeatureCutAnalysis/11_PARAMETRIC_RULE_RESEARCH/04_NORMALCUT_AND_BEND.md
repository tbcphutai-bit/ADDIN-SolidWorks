# NormalCut and Bend evidence

Ten native end-cut replays differed from a naive full-depth Sketch extrusion in the Bend region. Eight corrections measured 6.8327391146 mm³; two measured 69.998465477 mm³ (1618 fr2 Cut ordinal 3 and fr4 ordinal 2). Measured before/after differences identify these as genuine remaining-material corrections, not mesh artifacts.

For the orthogonal U family, the correction is parameterized by the cover's inner farWall plane s=Wcover-tcover, the opposite Web far tangent qf and the Web Bend depth tcover+Rcover. This independently derived cap reproduces all eight T1/T2 corner oracles. The larger stepped/inclined 1618 correction has not been generalized; its measured replay is research evidence only.

Offsetting only the straight Web seam left tangent point contacts at the Bend endpoints. First guard attempt produced a minimum gap about 0.07078775 mm. Far guard was moved by the full normal-to-axial shift. An R=0.05 mm variant then exposed a 0.05 mm near-Bend gap; the near guard must start at s=-g, not sn-gapShift. Both failed results are retained in history. The final rules are checked with exact ClosestDistance and Body2.Check2.

1618 fr3 contains a real ICE/legacy Boss after the Cuts, adding 1,579.986102057 mm³. Before/after Boss bodies and an independent analytic L-prism have symmetric difference 0. That is material addition, not a Cut. A cut-only Preview cannot recreate absent Boss stock. This multi-step family remains unsupported rather than protecting one copied literal Boss tool in production.

Technical note: analytic arcs are trimmed with CreateTrimmedCurve2 before CreateTrimmedSheet4 / CreateExtrudedBody. See [official ICreateArc guidance](https://help.solidworks.com/2023/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModeler~ICreateArc.html). MakeOffset supports surface bodies and is not used to dilate solids for assembly clearance.
