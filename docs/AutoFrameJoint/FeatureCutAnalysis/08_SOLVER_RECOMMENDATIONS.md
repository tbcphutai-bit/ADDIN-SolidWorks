# Recommendations for ChatGPT Code Owner

These are research recommendations for review, not a solver implementation or verified manufacturing rule.

1. Recognize actual Cut type via GetTypeName2 plus GetTypeName fallback for ICE; this dataset has 15 Instant3D Cuts that a Cut-only filter would miss. Do not treat CutListFolder or a localized feature name as an operation type.
2. Carry configuration provenance. T1 files were saved flat; request the intended folded configuration when obtaining geometry. Keep reference-space transforms alongside every Sketch and apply the component transform only once after occurrence mapping is verified.
3. Build a per-member ordered operation model. The two members may each contain terminal machining, hole patterns and unrelated cuts. Separate end-local candidates from internal slots/holes and confirm contact/occurrence before labeling a Cut as a corner rule.
4. Support open stepped profiles, FlipSideToCut, sheet-metal NormalCut, both-direction termination and separate inclined-plane trims. Fr1's small Cut112 uses its own inclined plane; a single global 45° cutter cannot represent that sequence.
5. Treat End Condition as the operative limit. Never clip Through All Both by stale GetDepth values. Three fr3 cuts terminate Up To Vertex; their reference coordinates are in JSON. Body scope is stage-specific.
6. Resolve the direction/contour gaps in 07 before deterministic replay. The available scalar and segment data are observed; a default-direction interpretation is still marked INFERRED.
7. Define region semantics from a real section: broad Web, Wall, outer/inner return Lip, and finite-thickness Bend with actual inner R. Preserve shape continuity at the bend. A bbox or whole-profile diagonal alone cannot prove KEEP/CUT for a region.
8. Preserve drawing priority and KCC scope. Do not turn member length or zero intersection volume into winner/loser or no-cut proof. Use exact occurrence geometry, terminal faces, contacts/gaps and drawing/product-class evidence.
9. Before approving a production rule, compare temporary predicted bodies to recorded reference cases with known pre-state stock. This study has final bodies and native operation parameters but no removed-volume difference ground truth.

The Add-in has not been edited or built. Code Owner should review the evidence before a separate solver task.
