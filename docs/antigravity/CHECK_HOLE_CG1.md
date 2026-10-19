# ANTIGRAVITY — CHECK HOLE / CG1
> Source of truth: branch `feat/check-hole-connection-groups-cg1` of `tbcphutai-bit/ADDIN-SolidWorks`.
> Created from `main` commit `549a03c5e2d79fec289327c0bfeef3ac7e8fd757`.
> Owner workflow: ChatGPT designs and codes; Antigravity checks the integration, builds with the actual SOLIDWORKS environment, tests WinForms and repairs **only** issues introduced by CG1.

## 0. What has already been coded on this branch

**NO ZIP OR PATCH SCRIPT IS REQUIRED.** The CG1 files and production integration have been committed directly onto this branch.

Files changed:

1. `Commands/CheckAssemblyHole.ConnectionGroups.cs` — **new** diagnostic-only engine extension. Operates on the *existing* matched `HoleStackResult` collection, groups their holes, records occurrence names, geometric end candidates and coverage counts. Does **not** infer a master/host or create a new production NG.
2. `Commands/CheckAssemblyHole.cs` — **one** integration statement after the existing `BuildReport(...)` call: `lastReport.ConnectionCoverage = BuildConnectionCoverage(holes, results);`
3. `Commands/CheckHoleResultsForm.cs` — adds `CheckHoleReport.ConnectionCoverage`, diagnostic count in summary, a `Nhóm liên kết` button and a **read-only** table dialog.
4. `ADDIN.csproj` — adds the new C# file to the explicit compile list.

Open these files and review the diff against `main` **before** making further changes.

## 1. Required behavior

CG1 is a non-invasive diagnostic pass:
```text
ReadAssemblyHoles -> MergeSameComponentCylinders
  -> InjectRecoveredDeformedHoles
  -> BuildCandidateStacks / BuildRecoveryCandidateStacks / BuildFarMisalignmentStacks
  -> EvaluateStack -> BuildPatternIssues
  -> BuildReport (existing production behavior)
  -> BuildConnectionCoverage (NEW, READ-ONLY)
  -> SetReport / CreateReportMarkers (existing production behavior)
```

In the detailed CHECK HOLE results view, a new button `Nhóm liên kết` opens a table containing:

- geometric group ID, OK/NG status **copied from existing `EvaluateStack`**;
- participating component occurrences and hole count;
- two *geometric axial end candidates* (not a confirmed host);
- `MasterDecision` that explicitly states master is unresolved/irrelevant to concentricity;
- matching source, maximum offset and angle;
- note explaining why a group is not proof of fastening intent.

The original Check Hole report still contains **OK / NG / Chưa ghép / Pattern / Thiếu lỗ** with the same production semantics. `CG1` never creates a missing-hole NG.

### Explicit limitations

- One `HoleStack` is a set of matched geometric axes, **not necessarily** a designed bolted joint.
- A Part can participate in several different connections. Never store a permanent single Master at Part level.
- The two ends along the canonical axis do **not** reveal the intended outside/inside order. No automatic host selection in CG1.
- Groups with a missing hole cannot be built solely from known matched hole stacks.
- Unmatched holes are `Chưa ghép`, **not** proven NG; zero NG does **not** guarantee full Assembly coverage.
- CG1 only reports component occurrences that have a recognized hole. Suppressed/envelope/unavailable/solid Part with no recognized hole cannot be assumed checked.
- CG1 does not compare diameter or design intent and does not modify any Part, Mate, or user sketch.

## 2. Hard safety boundaries

Do NOT change these existing methods while completing CG1:

- `ReadAssemblyHoles`
- `TryReadHoleCylinder`
- `MergeSameComponentCylinders`, `MergeSlotEnds`
- `BuildCandidateStacks`
- `BuildRecoveryCandidateStacks`
- `BuildFarMisalignmentStacks`
- `EvaluateStack`
- `BuildPatternIssues`
- `AppendMissingPatternIssues`
- `ReadSkippedPatternHoles`
- `CreateNgMarkerSketch` / old-marker cleanup.

Do NOT modify `BomTaskPaneControl.Designer.cs`, event handlers or unrelated add-in commands. Do NOT change the tolerances: 0.10 mm, 0.20 degrees, normal search 3 mm, recovery up to 25 mm, far search 30 mm. Do NOT merge into `main` or force-push. Do NOT silently change the calculation of NG.

Keep edits to these four source/project files unless an unforeseen build issue is demonstrated and reported in advance.

## 3. What Antigravity must do, in order

1. Open the repository, fetch latest refs and switch to **`feat/check-hole-connection-groups-cg1`**. Do not recreate the branch, do not unzip prior materials, and do not overwrite uncommitted local work.
2. Run `git diff main...HEAD -- ADDIN.csproj Commands/CheckAssemblyHole.cs Commands/CheckAssemblyHole.ConnectionGroups.cs Commands/CheckHoleResultsForm.cs`.
3. Inspect the new C# file for `private static BuildConnectionCoverage(List<HoleAxis>, List<HoleStackResult>)`; verify original OK/NG evaluation is never modified by it.
4. Open the solution in Visual Studio with the correct SOLIDWORKS interop DLLs.
5. Build `Debug|x64` and `Release|x64` (.NET Framework 4.8 / C# 7.3). If compilation fails due to missing SDK/Interop, report the environment blocker; do not remove the project references. Fix CG1 compile errors only.
6. Run CHECK HOLE in SOLIDWORKS with representative Assemblies; verify UI button, read-only table, summary and rerun/cancel handling.
7. Compare the original Check Hole output before and after CG1 using the **same** Assembly, component configurations and inputs.
8. Commit only targeted CG1 fixes to this branch. Provide commit SHA, diff, build logs, test results and environment limitations.

### Windows commands

```powershell
git fetch origin
git status --short
git switch feat/check-hole-connection-groups-cg1
git pull --ff-only origin feat/check-hole-connection-groups-cg1

git diff origin/main...HEAD -- ADDIN.csproj Commands/CheckAssemblyHole.cs Commands/CheckAssemblyHole.ConnectionGroups.cs Commands/CheckHoleResultsForm.cs
git diff --check
```

If `git status --short` shows uncommitted changes, STOP and preserve them before switching; do not `reset --hard`.

## 4. Required real-CAD regression cases

Use real SOLIDWORKS assemblies when possible. Record input, expected/actual result, elapsed time and snapshot/log. If unavailable, mark `NOT_TESTED` — never claim PASS.

| ID | Scenario | Critical assertion |
|---|---|---|
| T01 | Two properly concentric round holes | Original OK unchanged; one new diagnostic group |
| T02 | Offset of 0.09 mm | Within existing 0.10 mm position tolerance |
| T03 | Offset of 0.11 mm | Existing NG unchanged; no host blamed by CG1 |
| T04 | Two coaxial holes but angle > 0.20° | Existing behavior preserved (within pairing gate) |
| T05 | A–B–C layered relationship | One geometric stack when matching rules allow it; master unresolved |
| T06 | B–D connection independent of A–B–C | Distinct groups; no Part-wide Master assumption |
| T07 | Completely unmatched hole | `Chưa ghép` maintained; counted as unmatched |
| T08 | Missing pattern hole / skipped instance | Existing Missing NG count and markers unchanged |
| T09 | SLOT, curved boundary or recovered-deformed hole | Existing recognition and angle policy unchanged |
| T10 | Large Assembly, rerun, cancellation, model changed after result | No stale diagnostic data; model-state checks and cancel remain valid |

Also verify: clicking `Nhóm liên kết` never writes a Feature; existing `Vẽ NG` still creates the same marker and reports its state correctly. The old CSV format must remain the same.

### Mandatory comparison

Collect before/after for each sample: `LogicalHoles`, `StackCount`, `NgStacks`, `PatternNg`, `MissingHoles`, `UnmatchedHoles`, marker count and result list ordering. Any unexpected difference is a **blocking regression**.

## 5. Review checklist

- [ ] Branch source inspected; no ZIP required
- [ ] Builds Debug x64 and Release x64, or blockers documented
- [ ] Diagnostic `Nhóm liên kết` UI works; no production result changed
- [ ] No Part- or Component-level global master
- [ ] No false claim of exhaustive coverage
- [ ] T01–T10 executed or individually listed as NOT_TESTED
- [ ] Build/test summary and Git diff supplied
- [ ] No merge to main

## 6. Roadmap after CG1 (NOT part of current coding scope)

- **CG2 — ComponentContactGraph / ExpectedHoleCandidate:** determine physically related material layers and candidates even when a hole is absent. Evidence must be stronger than "Part inside Part"; no NG solely from a gap.
- **CG3 — Per-connection Master Hole:** infer when reliable from fastening direction and supported design relationships; otherwise `CHECK`; a single Part may act as master in one group and participant in another.
- **CG4 — production validation rules:** independently justify missing-hole/misalignment classifications; preserve `CHECK` as distinct from NG for insufficient evidence.
- **CG5 — large Assembly optimization and full CAD regression:** scoped scanning, performance profiling and interface completion.

Do not start CG2 automatically. Wait for explicit review of CG1.

## 7. Completion response template

```text
CG1 BRANCH:
COMMIT SHA:
CHANGED FILES:
Debug|x64 build:
Release|x64 build:
SOLIDWORKS / INTEROP version:
T01..T10 status: PASS / FAIL / NOT_TESTED, with evidence
Before/after: OK, NG, Missing, Pattern, unmatched, marker counts:
Known risks / unresolved:
Ready for review: YES / NO
```
