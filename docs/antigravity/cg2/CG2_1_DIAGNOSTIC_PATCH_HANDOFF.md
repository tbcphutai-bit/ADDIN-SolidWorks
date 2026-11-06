# CG2.1 — ChatGPT-authored C# patch, Anti applies directly to existing classes; NO BUILD

**Source baseline inspected:** 0c7c56999755ba3da9336f6e20b640f0c4c3c4f4  
**Repository:** tbcphutai-bit/ADDIN-SolidWorks  
**Branch:** feat/check-hole-cg2-part-relations / PR #33 Draft.

## Ownership and constraints

ChatGPT has written FOUR concrete, ready-to-apply unified-diff patch files. These are committed under docs/antigravity/cg2/ but the production .cs files have **not** been edited on GitHub by this handoff. Anti is authorized to apply them **to the existing C# class files in the local repository**. No duplicate classes, no new project source entries.

**NO AUTOMATIC BUILD, COMPILE, TEST, SOLIDWORKS START, POST-BUILD COPY, OR COMMIT/PUSH**. Build only after the user separately gives explicit permission. Keep the existing ADDIN.csproj Debug x64 OutputPath and PostBuildEvent exactly as they are.

DO NOT edit the production Check Hole NG decision, EvaluateStack, pattern logic, marker creation, CheckHoleCsv.Write, or any other command. The CG2 axis fields are diagnostic only, and BREP_SOLID_ON_AXIS still does NOT prove a required missing-hole NG.

## Four patches to apply together

1. CG2_1_PATCH_01_AXIS_DIAGNOSTIC.patch
   - Commands/CheckAssemblyHole.PartRelations.cs
   - New Cg2TryMeasureAxisSeparation() in assembly coordinates; row diagnostics TargetHoleAxisCount, NearestTargetAxisRadialMm and NearestTargetAxisAngleDeg. -1 means unavailable.
   - AxisToBoxMm remains the existing *axial distance to AABB*, NOT hole-to-hole radial offset.
2. CG2_1_PATCH_02_RUNNER_DIAGNOSTIC.patch
   - docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs
   - Exact occurrence accounting and warnings, coverage invariant; distinguishes CSV record ORDER_ONLY vs CONTENT_OR_COUNT difference; still fails strict byte parity.
3. CG2_1_PATCH_03_EXACT_SITE.patch
   - docs/antigravity/cg2/CG2_1_SITE_ASSERT.cs
   - Existing 9-column local TSV manifests remain accepted. Optional columns 10+11 independently constrain nearest hole-axis RADIAL offset (mm); wrong-offset self-test should fail.
4. CG2_1_PATCH_04_OFFLINE_TEST.patch
   - docs/antigravity/cg2/CG2_TEST_HARNESS.cs
   - Offline reflection test checks 0.30mm true radial axis separation at 15mm axial spacing.

## Anti procedure — one apply, no build

First inspect Git branch and local work. If any affected class already has local edits, STOP without losing them; do not use git reset/checkout -f/clean/force. Confirm the branch contains all four patch files and that the baseline source was not changed since the inspected SHA.

In PowerShell in C:\Tao moi\ADDIN:

    cd "C:\Tao moi\ADDIN"
    git branch --show-current
    git status --short

    $patches = @(
      "docs/antigravity/cg2/CG2_1_PATCH_01_AXIS_DIAGNOSTIC.patch"
      "docs/antigravity/cg2/CG2_1_PATCH_02_RUNNER_DIAGNOSTIC.patch"
      "docs/antigravity/cg2/CG2_1_PATCH_03_EXACT_SITE.patch"
      "docs/antigravity/cg2/CG2_1_PATCH_04_OFFLINE_TEST.patch"
    )
    git apply --check $patches
    if ($LASTEXITCODE -ne 0) { throw "PATCH_CONFLICT: Stop, send current class diff to ChatGPT." }
    git apply $patches
    if ($LASTEXITCODE -ne 0) { throw "PATCH_APPLY_FAILED: Do not build or force reset." }

    git diff --check
    if ($LASTEXITCODE -ne 0) { throw "DIFF_CHECK_FAILED" }
    git diff --stat
    git diff -- Commands/CheckAssemblyHole.PartRelations.cs docs/antigravity/cg2/CG2_LIVE_CAD_RUNNER.cs docs/antigravity/cg2/CG2_1_SITE_ASSERT.cs docs/antigravity/cg2/CG2_TEST_HARNESS.cs

If git apply --check fails: DO NOT force apply, overwrite, rebase or discard work. Report the conflicting method to ChatGPT.

## What must be checked in the diff WITHOUT compiling

- Only four C# source files changed; no production NG, CSV writer, GUI or project file.
- Exactly one Cg2TryMeasureAxisSeparation(), one AuditCg21Coverage(), existing site self-test expanded.
- Reflection property spellings in runner match the three new properties in CheckHoleCg2ExpectedHoleRow.
- Strict CSV failures still THROW and leave exit code 1.
- For no target hole, TargetHoleAxisCount=0 and NearestTargetAxisRadialMm=-1: DO NOT call this a proven missing-hole NG.
- No change in build paths: Debug x64 DLL / PostBuildEvent remains C:\Tao moi\ADDIN.dll.

**Stop and report: CODE_PATCH_APPLIED=YES/NO; GIT_DIFF_CHECK=PASS/FAIL; BUILD=NOT_RUN; CAD_TEST=NOT_RUN; COMMIT=NO.** Do not mark functional behavior PASS from code review.

## Later manual tests (ONLY when user explicitly requests Build)

1. Build manually via Visual Studio Debug x64 with original ADDIN.csproj (may copy DLL to old installed location through PostBuildEvent); record DLL SHA.
2. Recompile offline harness and runner manually only if user authorizes tests; no invisible automatic build.
3. In approved disposable Pack-and-Go sandbox test aligned axes and a **verified** 0.30mm target-axis offset; the 11-column manifest should use *independently measured* site geometry, not values copied from runner output. A candidate with AABB AxisToBoxMm=0 does not imply 0mm hole-to-hole offset.
4. Run existing CSV repeatability check; distinguish exact record reordering vs changed content, preserve FAIL if raw bytes differ. Do not claim the root cause until shown.
5. Validate TotalOccurrenceCount, EligibleComponentCount, ComponentsWithBounds, SubassemblyContainerCount, SkippedOrUnreadableCount from the SAME report.
6. Do not kill pre-existing SOLIDWORKS processes, do not alter production files or markers.
7. PR #33 remains Draft; READY_FOR_MERGE=NO and no CG3.

**Evidence level today:** static structure/context checks and patch-hunk replay against the inspected source only. No Windows C# build, SOLIDWORKS test or user installation occurred.
