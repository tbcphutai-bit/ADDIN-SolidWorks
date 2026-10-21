# Solver Recommendations

The following are `INFERRED` directions for Code Owner review, not validated manufacturing rules:

1. Keep A and B removal candidates independent; both Frames may require local trimming.
2. Resolve winner/loser from applicable drawing or confirmed KCC class, not member length alone. Preserve drawing priority and escalate out-of-scope cases.
3. Classify Web, Wall, Lip subtypes (outer silhouette, inward return, stiffener, double fold), and Bend R along the profile. Compute region-specific trim and clearance; avoid one fixed 45° plane.
4. Preserve component-local geometry and apply the assembly transform once with occurrence/configuration provenance.
5. Do not use zero overlap volume as proof that no joint or cut is needed; face/edge contact and gaps can still require processing.
6. Preview on temporary bodies only after selecting geometry rules. Compare against native before/after feature bodies before marking a rule verified.
7. Candidate review cases: 1618a fr1×fr2 and fr3×fr4, 3615a T1/T2 at 4/18/34 mm, and an edge-contact/gap case. These are proposals, not test results.

No Auto Frame Joint code or algorithm was changed.
