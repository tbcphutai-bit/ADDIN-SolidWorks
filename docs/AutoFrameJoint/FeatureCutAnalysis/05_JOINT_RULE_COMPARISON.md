# Joint Rule Comparison

`DOCUMENT_VERIFIED (KCC_SOURCE)`: “winner” is the side that hides the opposing sheet-thickness end face (`小口`); the losing side's end face is hidden. For the listed four-side bent panels and cut-plate panels, the short-side direction wins. For a square panel, select one side consistently. Drawing instructions take priority. Other conditions are referred to the preceding/following process owner.

The native trees now prove machining on both long and short Frames. They do not encode a universal winner/loser tag or prove KCC applies to every profile family. An internal slot, a patterned hole and an end trim have different purposes even when all originate as Cut-Extrude.

| Example | Verified native Cut count | Meaning / unresolved |
| --- | --- | --- |
| 1618a fr1 × fr2 | 4 / 4 | Both have cuts; fr1 includes separate inclined-plane trim. Occurrence/contact and winner remain UNVERIFIED. |
| 1618a fr3 × fr4 | 7 / 5 | fr3 includes three vertex-limited Cuts; fr4 also has blind/through machining. Different sequence/profile from fr1–fr2. |
| 3615a T1 fr1/fr2/fr3/fr4 | 4 / 3 / 4 / 3 | Folded Default read separately from saved flat state. No exact pair occurrence assignment. |
| 3615a T2 fr1/fr2 | 4 / 3 | Default folded state; long/short labels require actual occurrence orientation. |
| 2354 B11 | NOT_AVAILABLE | Exact source remains missing; prior gap/axis log is not cut geometry. |
| 4114 independent Part | 1 | Independent Cut-Extrude plus sheet-metal history; no supplied mating component. |

## Generalization supported by the examples

Use per-member operations, multiple sketch planes, stepped/open end profiles, sheet-metal NormalCut settings and reference-limited terminations. The presence of diagonal sketch lines does not justify a global 45° solid cut. Member-length comparison is metadata for a case, not evidence of winner/loser. Preserve drawing priority and KCC scope.
