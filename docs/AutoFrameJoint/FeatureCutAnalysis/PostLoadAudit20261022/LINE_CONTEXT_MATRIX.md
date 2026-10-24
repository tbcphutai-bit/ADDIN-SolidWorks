# Native Sketch segment context diagnostic

**API_VERIFIED, copied-CAD standalone harness; no solver or Cut.** Target: 2354 fr04 in the fr03/fr04 assembly. The diagnostic completed with a recorded Part-manager line-creation failure; this failure is retained in the JSON rather than presented as an all-pass test.

## Observed handle behavior

| Segment handle route | Select4 | Assembly / Part selected count | Allowed single-line relations |
|---|---|---|---|
| Direct return from Assembly-manager CreateLine | true | **0 / 1** | Empty |
| That return mapped through Part.Extension.GetCorresponding | true | **0 / 1** | Empty |
| Reacquired through original Assembly-context Sketch's Feature.GetSpecificFeature2().GetSketchSegments | true | **1 / 1** | **4,5,17** |
| Reacquired handle mapped through Component.GetCorresponding | true | **1 / 1** | **4,5,17** |

Both the Assembly-active and Feature-specific Sketch relation managers produced these results. Raw object packing also left the direct Assembly-created handle's allowed list empty. In this experiment, **Part-manager CreateLine returned null**; that is a bounded observation, not a statement that it always fails.

The effective reacquisition started from **the original active Assembly-context Sketch queried to Feature**, followed by `GetSpecificFeature2`. It did not obtain a different Feature by enumerating the native Part's tree. Segment IDs matched across the problematic and qualified handles, so IDs alone did not establish the required document context.

## Relation and geometry readback

Using the reacquired handle, the harness created one advertised Horizontal relation. Its actual native relation type was4 and the relation count changed from0 to1. Type17 was merely present in the allowed list; no FIX relation was created.

- Actual Web face attachment; correct target-Part Sketch ownership and restored assembly edit context.
- **0 dimensions and0 new reference planes**.
- Body symmetric difference before/after the diagnostic: **0 m³**.
- Original and copied file hashes unchanged; the owned copied documents were closed without Save and the isolated session returned blank.

The diagnostic Sketch remains **underconstrained**. This result identifies a viable segment-handle path for subsequent tests; it does not verify a complete cover profile, native Cut, parameter following, production Add-in integration or the reported Part-open hang.

Sanitized rows are in `LINE_CONTEXT_MATRIX.csv`; the complete selected result is in `LINE_CONTEXT_MATRIX.json`. Exact helper source and compile notes are under `evidence/`. No native CAD, COM reference tokens or machine-specific paths are included.
