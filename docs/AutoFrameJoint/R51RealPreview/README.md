# R51 — Profile cutters, non-manifold 547 and real Preview

Date: 2026-10-22. Review with [PR #40](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/40) and [PR #41](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/41).

**2354 fr04 × fr03: two modified temporary Frames pass geometry validation and Display3/Hide API. General Auto Frame Joint remains incomplete.** The installed Add-in was not built, rebuilt or deployed for this work. UI clicking after an owner build is not covered by the detached API results.

## What changed

Only `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` changes in production source. R50 UI, A/B selection, command entry and session manager remain unchanged in the user's checkout.

The previous local backend still used R48's `A ∩ B` → bisector → subtract the clipped overlap. R51 integrates the profile/rule pipeline into that existing class by replacing its candidate method and adding internal helpers. Original overlay, Display3/Hide ownership, public callbacks and cleanup methods are preserved; the local file is not replaced wholesale with PR #40.

```mermaid
flowchart LR
  AB[Read A/B copies] --> P[Classify API profile and B-rep skins]
  P --> R[Roles and verified rule scope]
  R --> T[Independent ordered tools A/B]
  T --> B[Boolean on disposable copies]
  B --> V[Body / KEEP / volume / gap / interference]
  V --> G[Display3 KEEP or KEEP plus CUT]
```

## Evidence summary

- **42 Cut definitions re-read** on 11 private Read-Only Parts; flags, forward end condition and depth match the prior extraction with zero differences. The previously captured 42 native before/after pairs are retained.
- Stored forward ends: **16 Blind, 22 Through All Both, 3 Up To Vertex, 1 Through All**. **32 NormalCut=true; 11 FlipSideToCut=true.**
- Of the 16 Blind records: **14 depth=t; 1 depth=t+0.10 mm; 1 depth=10 mm.** Active directions, LinkToThickness and start-plane position must also be considered. Non-Blind stored depth values are not interpreted as the actual cut travel.
- **Three suppressed 2354 Cuts discovered and studied** on new isolated copies. They use Through All/Through All Both and NormalCut. Their open/closed Sketches and state changes are recorded. Two strict volume-difference residuals remain unexplained; these are not silently marked verified.
- 2354 rule selection now uses the actual longitudinal body midpoint to identify the selected end, and verifies physical Web/Wall/Bend skins. It no longer rejects/accepts a profile from `ReverseThickness` alone.
- Unequal-height support is limited to a plain U where the shorter covering Walls reach the losing Wall heights. Unequal Return levels or a covering Wall that is too low are refused.
- **28/28 corner Cut replays match their native after bodies**, with zero measured symmetric difference; 14 curved hole/slot Cuts are excluded from corner replay. Ten replays need NormalCut Bend corrections; two other multi-tool cases are ordinary compound cuts.
- **12/12 dimension/placement controls pass**, plus three taller-cover variants with A/B reversal. Two lower-cover controls and an extra-layer single-sheet control are correctly refused. These are synthetic API B-rep controls, not additional native Parts.
- One U rule family has two scoped variants: equal-level `ORTHOGONAL_U_COVER` and plain-U `ORTHOGONAL_U_TALLER_COVER`. Historical replay templates are research evidence; they are not 28 generalized runtime rules.

## Required cases

| Case | Geometry | Display3 / Hide | Limit |
| --- | --- | --- | --- |
| 2354 fr04 × fr03, currently suppressed-Cut stock | `PASS_GEOMETRY`, A/B reversal checked | Both modes pass; cleanup passes | Generated shorter-cover policy differs from the historical suppressed Cut sequence. See comparison. |
| 1618 fr1 × fr2 | `UNVERIFIED_RULE` | `NOT_RUN`; no cut is displayed | No unique supported Web/parallel-Wall classification. |
| 1618 fr3 × fr4 | `UNVERIFIED_RULE` | `NOT_RUN`; no cut is displayed | Multi-step profile lacks a supported unique classification. |
| 3615 T1/T2: eight before-Cut native snapshot pairs | Eight `PASS_GEOMETRY` | Eight private rollback pairs pass both display modes and Hide | Read-Only copied Parts only; rollback restored without save. |
| 3615 current finished states | `UNVERIFIED_RULE` | No successful new-cut claim | Required KEEP stock is missing for the current shorter-cover policy. Cutting cannot add it. |
| Second old Debug pair | `NOT_IDENTIFIED` | `NOT_RUN` | No second distinct component pair was recovered; no test claim. |

See [CSV matrix](TEST_MATRIX.csv) and [full API results](TEST_RESULTS.json), including body checks, actual volume changes, plans and errors. Additional replay/variant results are linked below; their files are the authority for counts.

## Read next

1. [547 diagnosis and rejected approaches](01_ROOT_CAUSE_547.md)
2. [42 Feature audit, depth and NormalCut](02_FEATURE_CUT_AND_DEPTH_RULES.md), [CSV](FEATURE_CUT_AUDIT.csv), [JSON](FEATURE_CUT_AUDIT.json)
3. [KEEP/CUT geometry and 2354 comparison](03_KEEP_CUT_AND_2354_RULE.md)
4. [API test method and limitations](04_API_TESTS_AND_LIMITS.md)
5. [Manual source integration](05_MANUAL_INTEGRATION.md)

Structured evidence: [summary](FEATURE_CUT_SUMMARY.json), [2354 suppressed history](2354_NATIVE_CUT_STUDY.json), [native Cut replay](NATIVE_REPLAY_RESULTS.json), [test results](TEST_RESULTS.json).

Final-source 2354 rerun: [local integration](FINAL_2354_SMOKE_RESULTS.JSON), [Draft-branch variant](evidence/FINAL_BRANCH_2354_SMOKE.json). See [execution summary](EXECUTION_SUMMARY.json), [logs and harness provenance](evidence/README.md), and [file manifest](REPORT_MANIFEST.csv).

## Evidence levels

`API_VERIFIED`: direct Feature/Sketch/SheetMetal getters and call results. `GEOMETRY_VERIFIED`: measured temporary B-rep bodies, checks, volumes, distances and comparisons. `INFERRED`: manufacturing generalization beyond the tested geometry and interpretation of historical intent. A successful Display3 call is not human visual approval or a production machining approval.

No original CAD/STEP/ZIP, original PDF, binary, PDB or raw machine path is published. No merge.
