# Joint Rule Comparison

## KCC 26003

`DOCUMENT_VERIFIED (KCC_SOURCE)`: “winner” is the side that hides the opposing sheet-thickness end face (`小口`); the losing side's end face is hidden. For the listed four-side bent panels and cut-plate panels, the short-side direction wins. For a square panel, select one side consistently. Drawing instructions take priority. Other conditions are referred to the preceding/following process owner.

This does not mean every short Frame wins or that the losing member is never cut. It resolves end-face visibility/priority within the named classes, not a generic profile Cut command.

## Example comparison

| Example | Available comparison | Status |
| --- | --- | --- |
| 1618a fr1 × fr2 | Lower-left projection; different projected outlines | `INFERRED`; no occurrence mapping or Cut history |
| 1618a fr3 × fr4 | Upper-right projection; diagonal and straight edges shown | `INFERRED`; no operation attribution |
| 3615a T1/T2 | Reference chart at 4/18/34 mm; local CAD archive inventoried | `INFERRED`; not rechecked with a geometry kernel or FeatureManager |
| 2354 B11 | Log estimates 90° and 2.455 mm gap; winner/loser unconfirmed; no Cut geometry generated | `UNVERIFIED`; exact native source not found |
| 4114 Part | Independent Part only | `UNVERIFIED`; no joint or STEP proven |

No universal long/short Frame winner rule is verified for these profiles. Confirm a drawing or applicability of the KCC product class first.
