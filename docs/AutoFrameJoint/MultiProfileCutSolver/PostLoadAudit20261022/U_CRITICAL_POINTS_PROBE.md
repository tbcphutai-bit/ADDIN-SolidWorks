# U miter: four critical points verified through native geometry

**API_VERIFIED, standalone copied-CAD harness.** Pair: 2354 fr03 × fr04; current rule `ORTHOGONAL_U_TALLER_COVER`, short fr04 covers. This probe created four isolated points in a face-attached 2D Sketch on fr04's actual Web. It created no Cut.

| Native result | Value |
|---|---:|
| Fully constrained Sketch status | 3 |
| Direct Coincident relations | 8 |
| Dangling / overdefining relations | 0 / 0 |
| Dimensions / new reference planes | 0 / 0 |
| Observable external references | 6 |
| Correct target-Part Sketch ownership | Verified |
| Assembly edit context restored | Verified |
| Source and copied file hashes unchanged | Verified |

The four points follow the current U cut's critical datums:

- Near master point: short near Web/Bend tangent Edge and long inner near-Wall Face plane.
- Far master point: both Frames' far Web/Bend tangent Edges.
- Inner far-Wall/tangent point: short inner far-Wall Face plane and long far tangent Edge.
- Outer-Wall intersection: short inner far-Wall Face plane and long outer far-Wall Face plane.

Each native relation preserved its expected seed position. Coordinates were transformed from Assembly world through the inverse target-component transform and then the native Sketch transform.

**A Face-plane relation is not necessarily a finite surface contact.** The relevant Wall points were 2.60 mm from the finite trimmed Face while lying on its underlying plane; the API accepted those direct Coincident relations. The JSON and CSV retain `finite_trim=false` and measured distances. Tangent Edge references were verified on their finite trims.

This establishes the four-point relation mechanism. It does not establish a fully constrained closed cutting profile, a native Cut from that profile, actual parameter following, cycle safety after later Cuts, regeneration/reopen of a final Cut or installed Add-in behavior. Those remain separate qualifications. See `U_CRITICAL_POINTS_PROBE.json` and `.csv` for sanitized measurements.
