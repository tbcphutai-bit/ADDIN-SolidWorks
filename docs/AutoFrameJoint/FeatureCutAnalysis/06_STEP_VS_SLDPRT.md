# STEP vs SLDPRT

The 1618a STEP is AP214 text and contains six `MANIFOLD_SOLID_BREP`, seven `PRODUCT_DEFINITION`, and six `NEXT_ASSEMBLY_USAGE_OCCURRENCE` entities in a text-level scan. The supplied derived table reports six valid B-rep solids with volumes and bounding boxes. Body-to-occurrence/configuration/transform mapping is unavailable. STEP final-shape data does not encode native FeatureManager history.

3615a T1 STEP text contains five solid entities, six product definitions, and five assembly usage records. T2 contains three solid entities and five usage records, suggesting repeated references. These are structural text counts only; no geometry kernel or occurrence mapping was run.

No native Part opened successfully. Therefore no STEP shape was compared to ordered native history. The independent 4114 Part has no matching STEP in the supplied package. No before/after state was created and no feature was suppressed or rebuilt.

`GEOMETRY_VERIFIED` is limited to the supplied 1618a solid inventory's reported final B-rep validity/metrics and carries the provenance caveat. It does not verify a Cut, removed material, winner/loser, contact, or manufacturing correctness.
