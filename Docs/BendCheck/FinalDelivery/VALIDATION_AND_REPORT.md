# CHECK CHẤN — final executable validation plan and report contract

This document is a **test specification**. It does not assert these future tests were executed.
Baseline evidence: PR #34 and PR #35 (see their Validation-CompanyCadPairs.md and Validation-NativeExcelMatrix.md).

## Gate summary

| Gate | Required evidence | Failing / missing behavior |
| --- | --- | --- |
| G0 Fixed source | Exact 18/18 CadPairBinding.Matched, 18 distinct Punch+Holder roots; SHA pinned | No substitute geometry; fail closed |
| G1 Material | 18 reviewed Holder definitions or a per-tool explicit missing/unsupported inventory; no omitted source keys | UNKNOWN and review request |
| G2 Mount/setup | Independent datum, seating, real Punch/Holder axial spans, source-to-work, error bounds | UNKNOWN; simulation allowed only with separate explicit review |
| G3 Sheet/process | Registered whole-sheet before/after states, five-phase motion law, source revisions, all native FeatureIds | Process UNKNOWN; do not infer from finished Part |
| G4 Numeric | Independently checked oracle, per pair/phase/segment witness, errors, no false CLEAR/COLLISION | Numeric result FAIL; no automatic PASS |
| G5 Planner | Correct complete common-tool path under declared graph and budgets; partial/interrupt flags | No whole-Part/production winner |
| G6 Native | Debug+Release, build, owned PID, loaded actual DLL hash, clean server exit | Overall release FAIL even if function assertions pass |
| G7 UX/release | 18 candidates visible, truthful UNKNOWN/review/simulation, no Check Min regression | Keep Draft; no merge |

## Actual source baseline that must be preserved

- **18 CAD punch-holder pairs** in original Master source. Do not fabricate more or change their original LINE/ARC/POLYLINE/SPLINE/CIRCLE or INSERT transforms. KIK Punch is OPEN. User wants the pair as drawn; drawing WCS is NOT seating approval.
- **33 existing native Excel-derived Part fixtures and 72 native bends**: 18 U (2 bends), 9 Z (2 bends), 6 THREE (3 bends). Case IDs XL-01..XL-33, dimensions/source interpretation from PR #35. 4,200/6,000 mm are reference axial lengths; include below/at/above but do not infer capacity.
- **3 genuine Check Min NG**: XL-19, XL-20, XL-21 native FeatureId 53, ST3.2, A=15 mm / A min=15.2 mm. These MUST remain BelowMinimum; software PASS must not turn NG into MeetsMinimum.
- Last reported native 33-case all Punch/Holder/process statuses UNKNOWN, all numeric company queries ZERO, 18 NeedsEvidence per Part. Do not relabel these baseline facts.

## Numerical experiments — avoid counterfeit acceptance

Partition evidence:
1. **Topology inventory (CAD)**: only shape observations; no material role or hardware pose approval.
2. **Reviewed simulation (CAD)**: external signature explicitly SimulationOnly on actual CAD selection and explicit pose/axial span; numeric CLEAR/COLLISION/UNKNOWN only within declared model.
3. **Physical setup reviewed**: independently reviewed Holder roles, mapping, seating, axial segments, datum and machine constraints; static numeric can be checked.
4. **Physical process reviewed**: all stepwise workpiece states and continuous phase motion, source-bound; then process and whole-Part route may be evaluated in that declared scope.
5. **Production approval**: a separate company review beyond the current geometry engine including any out-of-scope machine tooling (e.g. V-die).

Run real independent math oracles for:
- nonintersecting, tangent, overlapping, holes, islands, SPLINE boundary bands;
- axial interval overlap/just-outside/uncertainty, mirrored/rotated transforms;
- continuous motion crossing that sparse sampled frames would miss;
- KIK OPEN, missing Holder role, invalid signature, expired review, wrong handle, swapped same-block holder, changed DXF byte/manifest/authority/setup;
- one valid collision witness for one tool (must not mark the entire Part impossible);
- complete CLEAR for declared path, incomplete path, graph coverage Bounded, CANCELLED/STALE with immediate result withdrawal;
- common tool with different per-FeatureId certificates but identical placed hardware;
- tool setup that changes between bends or phases (must not claim same tool).

Actual test fixtures may require human-supplied sources. If missing, mark G2/G3 BLOCKED; **do not use dummy private signatures or dummy datums** to claim real tools pass.

## Native execution — reproduce and measure

Use existing documented tools:
- Tests/BendToolProfiles/ToolProfiles.Tests.csproj
- ToolProfiles.Tests.exe --cad-pair-tests
- ToolProfiles.Tests.exe --cad-pairs-company <PRIVATE-PLACEMENT-JSON> <PRIVATE-OUTPUT>
- Tests/BendToolProfiles/Run-NativeExcelMatrix.ps1 -Sidecar <private> -OutputDirectory <new> -PartTemplate <private> -MinimumWorkbook <private> -PlacementConfiguration <private> -Configuration Debug
- Repeat Release in a different directory; explicit 11-case partitions permitted; merge must match all source hashes.

For 33 native cases, compare actual SOLIDWORKS B-rep/FeatureId/PersistentId/Min with existing tested references and actual 18 candidates per bend, cold/warm, Rebuild/Refresh, source SHA, original Part unchanged. For the newly reviewed numeric cases, capture actual solver queries; zero queries cannot count as numeric collision proof.

Native A/B (DO NOT deploy into user's daily installation):
1. Owned SOLIDWORKS PID with documented version.
2. Loaded in-process startup DLL path and SHA recorded.
3. Same isolated fixture on baseline vs new DLL; same startup mechanism.
4. Stack/dump/private lifetime traces on crash; classify app/product/harness/environment.
5. Close Form, cancel active work, disconnect, ExitApp; require observed zero exit within original time budget.
6. If exit status is nonzero/unavailable, release gate FAIL/NOT_CONFIRMED. Do not move timeout or downgrade check.

## Report JSON contract (private detailed export)

Suggested top-level fields (this is an **output** schema, not a new runtime proof):
~~~json
{
  "repoCommit": "<actual SHA>",
  "sourceMasterSha256": "<actual SHA or REDACTED public>",
  "fixtureMatrixSha256": "<actual SHA>",
  "solWorksBuild": "<observed version>",
  "loadedAddinPath": "<private>",
  "loadedAddinSha256": "<actual SHA>",
  "runId": "<unique immutable run ID>",
  "mode": "Debug or Release",
  "cases": [
    {
      "caseId": "XL-17",
      "bendIds": ["<actual native FeatureId>"],
      "toolCandidates": 18,
      "holderMaterialReviewCoverage": "<per-tool status>",
      "numericQueries": 0,
      "staticPunch": "UNKNOWN",
      "staticHolder": "UNKNOWN",
      "process": "UNKNOWN",
      "sequence": "NEEDS_EVIDENCE",
      "missingReasons": ["<actual reasons>"],
      "simulationOnly": false,
      "manufacturing": "CHUA_XAC_MINH",
      "functionalTestStatus": "PASS",
      "nativeShutdown": "NOT_CONFIRMED"
    }
  ],
  "nativeLifecycle": "PASS / FAIL / NOT_CONFIRMED",
  "limitations": ["<measured blockers>"]
}
~~~

The example is illustrative and not fabricated runtime output. Public report must redact customer paths, exact machine coordinates, review keys/dumps and CAD/Excel. Store private proof in local company files, link a sanitized Markdown report.

## PR review checklist / final answer from Codex

- [ ] Explain what is already implemented and what changed, with file/line anchors.
- [ ] Show **actual** 18/18 fixed CAD selections, Holder review status, 17 valid Punch / KIK OPEN.
- [ ] Show full tested results for XL-01..XL-33 and a detailed XL-17 walkthrough. Preserve all real NG.
- [ ] Show numeric queries and collision witnesses **only if actually executed**, label simulations.
- [ ] Show source hashes/revisions, leases, signed review status, in-scope declarations.
- [ ] Show full G0-G7 status PASS/FAIL/UNKNOWN/BLOCKED/NOT_RUN and raw failure summary.
- [ ] Show Debug/Release Windows build and managed/native tests, compared against original Check Min math.
- [ ] Show owned PID, actually loaded DLL path/hash and native shutdown A/B results.
- [ ] Explain what can be delivered now vs what requires the user's physical review, **without asking the user to recreate source CAD**.
- [ ] Give at least two final technical alternatives with a recommendation and tradeoff table.
- [ ] Keep PR Draft / no main merge until G6 and other required acceptance gates actually pass.
