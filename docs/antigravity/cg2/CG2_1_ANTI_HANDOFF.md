
# UPDATE — ONE-SHOT AUTO BUILD + CAD RUNNER + EXACT SITE TESTS

This section OVERRIDES any earlier manual multi-step build commands below. ChatGPT
has already committed all code. Anti must never paste a duplicate partial class,
write C#, or edit a PowerShell script. PR #33 remains DRAFT, READY_FOR_MERGE=NO.

## New files already committed

- docs/antigravity/cg2/CG2_1_ONE_SHOT.ps1 — one batch for Git fast-forward
  pull, MSBuild Debug+Release x64, compiled offline harness, compiled CAD runner,
  runner self-test, optional sandbox fixtures, and repeatability.
- docs/antigravity/cg2/CG2_1_SITE_ASSERT.cs — source-to-target-to-hole
  checks using a fixture-specific manifest. Included in standalone CAD runner,
  NEVER in ADDIN.csproj.
- docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs — enhanced logs of BREP
  material status, hits, span, Subassembly containers, unresolved Parts,
  EVEN when raw CSV comparison fails. Raw CSV FAIL still produces exit 1.

## Anti preflight

1. Open Windows Developer PowerShell in CG2 repository root (typically
   C:\Tao moi\ADDIN), with a CLEAN worktree, on branch
   feat/check-hole-cg2-part-relations.
2. Identify the real stored CG1 DLL (the prior report used
   C:\Tao moi\backup_cg1_release_snapshot\ADDIN.dll).
3. Verify disposable Pack-and-Go fixtures, with all dependencies under a
   sandbox directory (the prior report used D:\CG1-ISOLATED-TEST-COPIES).
   Explicit CG1_SANDBOX_OK.txt MUST ALREADY EXIST. Do not auto-create it.
4. Close the user's SOLIDWORKS sessions manually before the runner; never
   disable existing process/model/marker/reference safeguards.
5. Supply only verified existing SLDASM fixture paths, not guessed paths.
6. Verify VS MSBuild and Roslyn C# compiler, and SOLIDWORKS interop DLLs.
   The one-shot script discovers these from PATH/vswhere and project
   reference hints, respectively.

## The ONE command block

Open PowerShell in your repository and paste this block ONCE, after
substituting confirmed fixture paths (remove any that do not exist):

    cd "C:\Tao moi\ADDIN"
    $root = "D:\CG1-ISOLATED-TEST-COPIES"
    $cg1 = "C:\Tao moi\backup_cg1_release_snapshot\ADDIN.dll"
    $fixtures = @(
        "$root\Fixture_BREP_Positive.SLDASM"
        "$root\Fixture_OK.SLDASM"
        "$root\Fixture_NG_Offset.SLDASM"
        "$root\Fixture_Unmatched.SLDASM"
        "$root\Fixture_MultiLayer_ABC.SLDASM"
    )
    # Append verified nested/rotated/916-hole fixture paths if approved.
    & ".\docs\antigravity\cg2\CG2_1_ONE_SHOT.ps1" -RepoRoot (Get-Location).Path -BaselineDll $cg1 -SandboxRoot $root -Fixtures $fixtures -RepeatabilityFixture "$root\Fixture_OK.SLDASM" -RunCad
    Write-Host "ONE_SHOT_EXIT=$LASTEXITCODE"

If the sandbox or CG1 baseline is not ready, run OFFLINE tests only
(the CAD and exact fixture assertions are NOT_TESTED, not PASS):

    cd "C:\Tao moi\ADDIN"
    & ".\docs\antigravity\cg2\CG2_1_ONE_SHOT.ps1" -RepoRoot (Get-Location).Path
    Write-Host "ONE_SHOT_EXIT=$LASTEXITCODE"

The script uses a temporary output folder instead of overwriting the
project's hardcoded Debug DLL path at C:\Tao moi\ADDIN.dll. It writes a
CG2_1_ONE_SHOT_SUMMARY.txt file and per-stage *.log under
%TEMP%\CG2_1_ONE_SHOT_<timestamp>. NO automatic Git commit or upload.
It fails/returns a nonzero exit code when a mandatory stage fails;
independent repeatability may still run after CSV regression failure.

## Required local exact-site manifest

To PROVE a specific Part B site, create inside sandbox:

    <sandbox-root>\CG2_1_EXPECTED_SITES.tsv

Nine TAB-separated columns, one expected site per line, with optional
comment lines starting with #:

    FixtureFileName [TAB] SourceOccurrence [TAB] TargetOccurrence
    [TAB] SourceHoleLabel [TAB] ExpectedEvidence [TAB] MinSpanMm
    [TAB] MaxSpanMm [TAB] MinHits [TAB] MinSubassemblyContainers

Only examples of values (REPLACE with exact IDs observed in the user's
approved fixtures, do not treat these as real test evidence):

    Fixture_Nested.SLDASM <TAB> PartA-1 <TAB> PartB-1@Subassembly-1 <TAB> Hole#1 <TAB> BREP_SOLID_ON_AXIS <TAB> 2.9 <TAB> 3.1 <TAB> 2 <TAB> 1
    Fixture_Offset.SLDASM <TAB> PartA-1 <TAB> PartB-1 <TAB> Hole#1 <TAB> CHECK_ONLY <TAB> 0 <TAB> 10000 <TAB> 0 <TAB> 0
    Fixture_Void.SLDASM <TAB> PartA-1 <TAB> PartB-1 <TAB> Hole#1 <TAB> BREP_NO_AXIS_HIT <TAB> 0 <TAB> 0 <TAB> 0 <TAB> 0

Replace the literal string <TAB> in examples with a real tab character.
Exact occurrence names and Hole# values must come from the model itself.
A wildcard * label is only accepted when a source-target pair has exactly
ONE candidate; otherwise site assertion FAILS. A missing manifest prints
CG2_1_EXACT_SITE_ASSERT=NOT_TESTED, not PASS. CHECK_ONLY does NOT prove a
missing hole, and BREP_SOLID_ON_AXIS does not prove fastening intent.

## One-pass verification matrix

- Ordinary Part aligned holes and 0.30mm/2mm displaced counterparts.
- Intermediate Part B with one specific absent hole (site assertion needed).
- Solid plate hit with 3mm/2mm spans and empty/void target.
- Nested Parts in a Subassembly, repeated Part occurrence and rotation:
  exact source/target/hole and nonzero Subassembly container coverage.
- The historically 916-hole Assembly if it is safe/available:
  elapsed time, BREP positives/no-hits/unknown/NOT_TESTED_BUDGET,
  NOT_TESTED_TIME_BUDGET, coverage and COM errors.
- Regression CG1 vs CG2 production holes, stacks, NG, missing, pattern, rows.
  Report raw CSV byte equality SEPARATELY; historical three of five raw
  comparisons failed. Never rebrand a byte FAIL as PASS.
- MARKER_GENERATION_PARITY=NOT_TESTED with AutoCreateMarkers=false.

## Status and reporting rule

The new code is committed but has NOT been built or run by ChatGPT on
Windows SOLIDWORKS. Source review is not runtime proof.

Anti updates ONLY CG2_REPORT.md and CG2_ANTI_CODE_STATUS_REPORT.md with
actual Windows logs, DLL SHA256, commit SHA, runner exit codes, fixture
assertion PASS/FAIL/NOT_TESTED, and elapsed seconds. DO NOT push any
sensitive raw CAD/CSV/logs or DLLs. ONE report-only commit on CG2 branch,
one PR #33 comment, then STOP. If any C# bug is found, STOP changing C#:
ChatGPT writes the next patch.

---
## Historical CG2.1 handoff text (superseded where overlapping)

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
