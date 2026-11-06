# Two separate 0.1 mm conventions

The user confirmed both conventions in this session:

| Quantity | Value | Meaning |
|---|---|---|
| Single-sheet Cut-Extrude depth | t + 0.1 mm | Cut through the local sheet thickness with machining overrun |
| Assembly fit clearance | fixed 0.1 mm | Clearance between the two Frame results |

They are independent parameters. A Blind feature depth recorded by the native API is never rewritten to conform to either convention. BLIND_DEPTH_CONVENTION.json retains the measured depths and agreement/disagreement. Through All / Both values are stored scalars, not finite Blind limits. Up To Vertex is resolved by the reference endpoint, not by t+0.1.

Preview creates no SOLIDWORKS Cut feature. A full-profile temporary mask may span the formed section and several sheet faces. That computational mask depth is not a manufacturing single-sheet extrusion depth. Tool closure overrun and assembly clearance are stored separately as ToolOverrun and AssemblyGap. Local future single-sheet feature authoring must use its own surface normal and t+0.1 mm; it is not implemented by this Preview-only patch.

The production Web seam, Wall end planes and Bend guards apply the fixed assembly clearance. Exact minimum body distance is validated after all Boolean operations. Historical native bodies contain contact and local relief and therefore are not claimed to satisfy a uniform pre-existing 0.1 mm assembly gap.
