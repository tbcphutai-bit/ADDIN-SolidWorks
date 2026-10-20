# CG2.1 — CHATGPT CODE PATCH / ANTIGRAVITY ONE-SHOT BUILD & CAD TEST

**Scope:** Only branch `feat/check-hole-cg2-part-relations`, PR #33 (Draft). **NO MERGE, NO CG3.**

## A. Ownership and source rules

- ChatGPT has **already committed the complete C# patch directly to the branch**. Antigravity **must not write/edit/refactor C#**. Only pull the branch. Do **not** paste a second copy of any method or partial class.
- If local changes exist, STOP and preserve them. Do not force reset or overwrite without approval.
- Anti may execute builds/tests on a separately approved disposable Pack-and-Go sandbox, collect sanitized evidence, and edit only the 2 verification reports after testing.
- Error during build or CAD execution: STOP, paste actual compiler/error message, exact file+line, API version, fixture, and exit code. ChatGPT writes any follow-up patch.

## B. Implemented changes — source of truth is C# files on branch

1. `Commands/CheckAssemblyHole.PartRelations.cs`
   - `Cg2IsResolvedPartOccurrence` distinguishes resolved `PartDoc` occurrences (physical targets) from `AssemblyDoc` containers and unknowns.
   - `AssemblyDoc.GetComponents(false)` includes subassembly descendants, so only Part children are physical AABB targets; containers are counted in `SubassemblyContainerCount` and not misreported as solid material.
   - Unresolved entries -> `UnresolvedLeafOccurrenceCount`, partial coverage; do not claim no hole or NG.
   - `Cg2LikelyKnownCounterpart` suppression now uses `PositionToleranceM=0.10mm` and `AngleToleranceDeg=0.20deg`, rather than 4mm/3deg. Near-but-misaligned axes must remain CHECK_ONLY candidates.
   - 60s *cooperative* AABB scan time budget; capped/partial results -> `PARTIAL_UNVERIFIED`. COM call itself cannot be interrupted inside a single invocation.
2. `Commands/CheckAssemblyHole.MaterialEvidence.cs`
   - Part-only lookup for nested leaf occurrences, 60s *cooperative* time budget for lookup/ray screening; untested -> `NOT_TESTED_TIME_BUDGET`, partial.
   - Preserve proven COM-9/COM-6 parser, 500 ray cap, fail-closed `UNVERIFIED_*`, `BREP_SOLID_ON_AXIS` is diagnostic only.
3. `Commands/CheckHoleResultsForm.cs`: read-only coverage counters for Subassembly containers excluded and unresolved Part occurrences.
4. `docs/antigravity/cg2/CG2_TEST_HARNESS.cs`: code assertions for 0.08mm/0.10deg suppressed, 0.30mm not suppressed, 2mm/2deg not suppressed, method/field/budget reflection checks.

**Not changed:** `CheckAssemblyHole.Run()`, `EvaluateStack`, pattern, missing hole logic, production NG thresholds, CSV writer, marker creation, main, CG1 branch.

## C. One test batch for Anti — in the following order

1. Verify branch and commit, clean worktree, source SHA and DLL SHA256. Suggested commands:
   ```powershell
   git fetch origin
   git switch feat/check-hole-cg2-part-relations
   git pull --ff-only origin feat/check-hole-cg2-part-relations
   git status --short
   git rev-parse HEAD
   ```
   If local changes or conflicts arise, stop. Do not force clean or discard them.
2. Build with the **installed Visual Studio MSBuild**, both `Release|x64` and `Debug|x64`, using the actual `ADDIN.csproj`. Record full command, build output, warning/error count and process exit codes.
3. Rebuild the standalone `CG2_TEST_HARNESS.cs` from current source using the existing repo build/test procedure, **not a cached EXE**; pass the current `ADDIN.dll`. Require zero failures on the new narrow counterpart tests and the COM-6/9 parser assertions. These are UNIT assertions, **not integrated Assembly proof**.
4. On **disposable isolated Pack-and-Go fixtures**, not production data, with marker creation disabled:
   - **NESTED_PART:** One assembly contains Subassembly with a resolved Part B; A has a through-hole, B's solid crosses the hole axis. Verify candidate TargetOccurrence is **the actual nested Part B occurrence**, not the Subassembly container. Verify `SubassemblyContainerCount>=1`, material evidence positive (if valid solid), and no invented NG.
   - **OFFSET_CANDIDATE:** Same fixture, add/check a real counterpart hole 0.30mm offset from A's axis; require a CG2 `CHECK_ONLY` candidate at that specific site (previous 4mm rule suppressed it). Measure source/target identifiers; don't infer site correctness from aggregate counts.
   - **BREP_POSITIVE:** Repeat known 3.000mm solid and 2.000mm reverse positive samples. Expect matching ENTER/EXIT events, no topology/layout exceptions. Report actual candidate IDs, source/target, hit counts, spans.
   - **BREP_NEGATIVE:** Matching hole/void axis; never label the no-material site as confirmed missing hole, and no CG2 NG.
   - **NESTED_ROTATED:** Two occurrences of the same Part file under different Subassembly instances with different placement/rotation; assert per-occurrence identity, body coordinate calibration and target attribution. If unresolved, label NOT_VERIFIED and give reason.
   - **LARGE_ASSEMBLY:** Run 916-hole assembly if approved, measuring `TotalOccurrenceCount`, `SubassemblyContainerCount`, `UnresolvedLeafOccurrenceCount`, `ComponentsWithBounds`, BREP counts, `NOT_TESTED_TIME_BUDGET`, `NOT_TESTED_BUDGET`, wall seconds, API errors. Time budgets are cooperative, not hard guarantees.
5. Regression: for **same fixture/config/sandbox copy**, compare CG1 baseline vs CG2.1 production Holes, Stacks, NG, PatternNG, Missing, Rows and canonical row signatures. Report strict raw CSV byte parity **separately**. Known 3/5 strict CSV failures must NOT be falsely relabeled PASS. `MARKER_GENERATION_PARITY=NOT_TESTED` if `AutoCreateMarkers=false`.

## D. Report and stop

Update **only** `docs/antigravity/cg2/CG2_REPORT.md` and `CG2_ANTI_CODE_STATUS_REPORT.md` with actual CG2.1 run evidence. No test result should say PASS without assertion/log and a matching executable/DLL SHA.

Required summary:
```text
CG2_1_SOURCE_HEAD=<full sha>
C_SHARP_EDITED_BY_ANTI=NO
BUILD_DEBUG_X64=PASS|FAIL|NOT_TESTED
BUILD_RELEASE_X64=PASS|FAIL|NOT_TESTED
OFFLINE_PATCH_TEST=PASS|FAIL|NOT_TESTED
NESTED_PART=PASS|FAIL|NOT_TESTED
OFFSET_CANDIDATE=PASS|FAIL|NOT_TESTED
BREP_POSITIVE=PASS|FAIL|NOT_TESTED
BREP_NEGATIVE=PASS|FAIL|NOT_TESTED
NESTED_ROTATED=PASS|FAIL|NOT_TESTED
LARGE_ASSEMBLY=PASS|FAIL|NOT_TESTED
CG1_VS_CG2_PRODUCTION_VERDICTS=PASS|FAIL|NOT_TESTED
STRICT_RAW_CSV_BYTE_PARITY=PASS|FAIL|NOT_TESTED
MARKER_GENERATION_PARITY=NOT_TESTED
COMPILLED_DLL_SHA256=<value or NOT_TESTED>
RUNNER_EXIT_CODE=<value or NOT_TESTED>
READY_FOR_MERGE=NO
```

Push one verification report commit on CG2 branch, post one short PR #33 comment with full SHA and report links, **then STOP for ChatGPT source/evidence review**. No CG3; no main merge.
