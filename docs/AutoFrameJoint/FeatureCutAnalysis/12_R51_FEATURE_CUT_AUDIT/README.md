# R51 — Feature Cut / NormalCut update, 2026-10-22

The previous 15-Part / 43-Cut Feature Tree extraction and 42 native before/after records remain unchanged. This addendum re-reads the 42 studied Cut definitions on 11 new private Read-Only copies and publishes the current audit. Add-in source and the complete test/log package are on [Draft PR #42](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/42), branch `fix/afj-r51-real-preview-nonmanifold-547`, commit `450f79b9ef34261ef8d8e8b68c6282e8f25c5712`.

## API and B-rep results

- 42 scalar/Sketch records match prior flags, forward ends and depths; copied-file hashes are unchanged.
- 16 Blind, 22 Through All Both, 3 Up To Vertex, 1 Through All; 32 NormalCut=true and 11 FlipSideToCut=true.
- Blind stored depths: 14=t, 1=t+0.10 mm, 1=10 mm. The t+0.10 record is 1618 fr4 Cut ordinal 5 (2.00 to 2.10 mm). Stored non-Blind depth is not actual travel.
- 28 corner Cut replays match native after B-reps with zero measured symmetric difference. Ten need extra NormalCut Bend corrections; two other multi-tool sequences are ordinary compound cuts. Fourteen curved hole/slot Cuts are excluded from corner replay.
- Two 2354 Parts have three suppressed historical Cuts. All three use NormalCut and Through All/Through All Both. Their copied states were restored without save. Two strict removed-volume residuals remain UNVERIFIED.

`API_VERIFIED` covers direct Feature/Sketch/SheetMetal getters. `GEOMETRY_VERIFIED` covers measured B-rep differences and validation. General manufacturing intent beyond the tested geometry remains `INFERRED`.

## Solver link and limits

SheetCutOverrun=0.10 mm and AssemblyFitGap=0.10 mm are independent conventions. The planar single-sheet factory is guarded against an unhandled Bend or second layer; formed-U masks use explicit relief and are not all converted to shallow Blind cuts.

R51's 2354 fr04×fr03 generates two validated cut Frames, with 0.10 mm gap, zero interference, A/B reversal and Display3/Hide passes. It follows the current shorter-cover policy and differs from the historical suppressed Cut sequence. It is not a native-identical or manufacturing-approved result. Eight 3615 before-Cut pairs pass; their current finished states lack required KEEP for this policy and are refused. Both requested 1618 pair rules remain UNVERIFIED_RULE. The second old Debug pair remains NOT_IDENTIFIED/NOT_RUN.

See [audit CSV](FEATURE_CUT_AUDIT.csv), [structured audit](FEATURE_CUT_AUDIT.json), [fresh Feature Trees](FRESH_FEATURE_TREES.json), [depth/NormalCut rules](02_FEATURE_CUT_AND_DEPTH_RULES.md), [native replay](NATIVE_REPLAY_RESULTS.json) and [2354 geometry](03_KEEP_CUT_AND_2354_RULE.md). Full source, matrices, controls and sanitized logs are in PR #42. No Add-in source, CAD, binaries or private machine paths are added to this documentation branch. No main Add-in build/deploy or merge.
