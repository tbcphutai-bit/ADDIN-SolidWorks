# Input Inventory — current API read

All 15 Parts returned non-null ModelDoc2, type 1 (Part), matching the requested file path and Read-Only=true. Full SHA256 and native metadata are in JSON; all source hashes matched before/after.

| Part | Config read | Top-level | Cut | Bodies | SHA256 prefix | Read-only/hash |
| --- | --- | --- | --- | --- | --- | --- |
| 1618-04-bh-A6-fr1.SLDPRT | Default | 37 | 4 | 1 | 2bd2094b6e5a76b9 | true / unchanged |
| 1618-04-bh-A6-1.SLDPRT | Default | 44 | 0 | 1 | 93eb041958540e6d | true / unchanged |
| 1618-04-bh-A6-fr2.SLDPRT | Default | 34 | 4 | 1 | 4103cb31b9dd7a32 | true / unchanged |
| 1618-04-bh-A6-fr3.SLDPRT | Default | 45 | 7 | 1 | 462e585d0ae53930 | true / unchanged |
| 1618-04-bh-A6-fr4.SLDPRT | Default | 36 | 5 | 1 | fe61216b8caa96f6 | true / unchanged |
| 1618-04-bh-A6-中骨1.SLDPRT | Default | 25 | 1 | 1 | 375ecfa994c9a644 | true / unchanged |
| 3615-04-bh-T1-1.SLDPRT | Default | 23 | 0 | 1 | 917ff98ffa3b4f46 | true / unchanged |
| 3615-04-bh-T1-fr1.SLDPRT | Default | 36 | 4 | 1 | 5cfbc53f38748af6 | true / unchanged |
| 3615-04-bh-T1-fr2.SLDPRT | Default | 32 | 3 | 1 | faa80073631ca9b3 | true / unchanged |
| 3615-04-bh-T1-fr3.SLDPRT | Default | 36 | 4 | 1 | 1799d1e2cb5c67ef | true / unchanged |
| 3615-04-bh-T1-fr4.SLDPRT | Default | 32 | 3 | 1 | f8be37cb822c1e2d | true / unchanged |
| 3615-04-bh-T2-1.SLDPRT | Default | 23 | 0 | 1 | 3acaa0b157c8f336 | true / unchanged |
| 3615-04-bh-T2-fr1.SLDPRT | Default | 36 | 4 | 1 | 79f91b1ed43b99a9 | true / unchanged |
| 3615-04-bh-T2-fr2.SLDPRT | Default | 32 | 3 | 1 | dc326ff6e051a79c | true / unchanged |
| 4114-04-HN-C07-12_New.SLDPRT | ﾃﾞﾌｫﾙﾄ | 32 | 1 | 1 | 126a7a288bab23a1 | true / unchanged |

Configuration names and per-configuration suppression are recorded. Parameters and geometry belong to the named active configuration only. The four T1 Frame files initially opened in DefaultSM-FLAT-PATTERN; a separate Read-Only open requested Default to read folded state. No ShowConfiguration2 or suppression setter was used.

The 4114 Part is an independent small sheet-metal example, not a confirmed member of a supplied joint. No exact 2354 B11 source was found in the earlier inventory. Three sheet/panel Parts have zero explicit Cut-Extrude nodes; their trees are present, so this is not NO_FEATURE_HISTORY. Hole/Library/Pattern feature types remain in the inventory and are not silently counted as corner cuts.

`4114`: save flag changed false→true during getter traversal. The reader closed its newly opened document without saving; source SHA256 remained identical. Other source records kept save flag false→false. This flag transition is reported rather than claiming every in-memory state stayed unchanged.
