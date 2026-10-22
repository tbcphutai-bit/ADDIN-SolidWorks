# Auto Frame Joint — OK creates native Cuts

Date: 2026-10-22. Based on R52 Draft PR #43. This branch implements the user's clarified request: **OK / “Áp dụng cắt và kết thúc” creates real Cut-Extrude features from the last validated Preview**. Cancel/Esc only cleans up.

## Verified results and remaining work

| Check | Result |
|---|---|
| Final 2354 fr04 × fr03 public GhostPreview → SessionManager → native writer | PASS in SOLIDWORKS API harness; 4 actual Cuts, both final symmetric differences 0 |
| Final 2354 pair validation | PASS: target gap 0.10 ± 0.001 mm; interference ≤ 1e-12 m³; final public-manager scalar DTO was not exported |
| Deliberate failure after native edits on both Parts | PASS: 4 Cuts removed; original geometry and Feature Tree restored |
| Stale/consumed Preview | Rejected before another native Cut |
| Managed callback/lifetime controls | 47/47 for local source and 47/47 for branch source; these are proxies |
| 1618 fr1×fr2, fr3×fr4 | Native solver refuses `UNVERIFIED_RULE`; no Part history change |
| 3615 T1/T2 current trimmed inputs | Native solver refuses `REQUIRED_KEEP_MISSING`; no Part history change |
| Earlier T1 stock-copy run | 4 native Cuts, zero symmetric difference, 0.10 mm gap; different run/context from final attempt |
| Final isolated T1 stock preparation | **FAIL**: `0x80010105 RPC_E_SERVERFAULT` reading Feature type before native writer |
| Final T2 stock + suppressed duplicate scope | **NOT_RUN**: batch stopped at T1 fault |
| Actual installed Add-in green OK / button106 | **NOT_RUN**; no main Add-in Build/Deploy |
| New Cut flatten/unflatten / native NormalCut=true qualification | **NOT_RUN** |

This is an implementation with native 2354 geometry evidence. It is not an acceptance of all Frame families or of the installed DLL integration. Read [API results](API_RESULTS.json), [matrix](TEST_MATRIX.csv), [failure limits](04_FAILURES_AND_LIMITS.md) and [manual integration](05_SOURCE_DLL_AND_MANUAL_INTEGRATION.md).

## Source changes

- `AutoFrameJointSelectionPage`: capture explicit OK intent; apply only after native page closes; managed selection revision prevents applying an old A/B pair; Finish re-reads selection outside native callbacks.
- `AutoFrameJointCommandEntry`: existing callback chain delegates apply and reports the result.
- `AutoFrameJointGhostSessionManager`: prepares the reviewed ticket, hides Ghost bodies before mutation and retains ownership while applying.
- `AutoFrameJointGhostPreview`: prepares a single-use source-verified ticket; contains the appended native Sketch/Plane/Cut writer and bounded rollback by owned Feature IDs.

No new command, second Preview, project entry, output path, deployment or registry change. Existing R50 Caption protection and R52 Preview cache remain. R51 analyzer/rules/plans/cutters/temporary Boolean/validation core is unchanged; see [hash proof](evidence/R51_CORE_INVARIANCE.json).

## Evidence

- `API_VERIFIED`: real SOLIDWORKS API documents, native features/definitions and returned values.
- `GEOMETRY_VERIFIED`: native B-rep checks, symmetric difference, gap and interference.
- `INFERRED`: conclusions that have not been measured directly. Managed proxy passes are explicitly separate.

[Native Cut inventory CSV](FEATURE_CUT_INVENTORY.csv) and [JSON](FEATURE_CUT_INVENTORY.json) contain 12 logged creation events: 4 final public 2354 Cuts, 4 rollback-test 2354 Cuts, 4 earlier successful T1 Cuts. Eight events have the complete exported native definition DTO. Public-manager events have native creation/shape logs; unexported depth/flags are `NOT_AVAILABLE`, not copied from another run.

The earlier 42 before/after studies and 43-Cut inventory are retained in their original reports: [R51 research](../R51RealPreview/README.md) and [before/after data](../ProfileAwareCutSolver/FEATURE_CUT_BEFORE_AFTER.json). They are not counted again as new native-write qualifications.

## Data protection

Four existing local source classes were applied with fresh SHA guards; user branch/HEAD, project and installed DLL/PDB were preserved. The installed DLL does not yet contain this writer. Tests used disposable CAD copies. All measured input hashes stayed unchanged. An earlier T1 run changed two disposable Part files on disk despite no Save API call by this code; that observation remains explicit. No CAD/B-rep, original ZIP, DLL/PDB, private machine paths or credentials are included.
