# Unified Assembly Batch integration

Branch: `feature/check-chan-unified-integration`.

The branch starts at PR #26 `fefcbf9475f42f197aabf61244d7da77c99cb6e6`
and merges PR #25 `c0507bcaf8ecc525167d6223a217205984f7c8a9` in merge commit
`344d000`. Product implementation and initial regression additions are in
`8cde0d7`; subsequent commits extend native diagnostics and test scopes.
No remote baseline branch or pull request is merged or overwritten.

## Merge resolution

There were **zero textual conflicts**. Git automatically combined
`ADDIN.csproj` and `Tests/BendToolProfiles/NativeAssemblyBatchValidation.cs`.
The project retains both the live UI compile entries and the Phase 4A.2 process
performance source/test entries. The native harness retains both performance
diagnostics and UI entry points. No file or regression was removed to make the
merge succeed. Debug/Release x64 builds validate the resulting project graph.

The Phase 4A.2 collision/envelope/process implementation is byte-for-byte the
same as PR #25. The integration does not modify Check Min math, the Antigravity
Designer, CAD/Excel, or Phase 4B/4A algorithms. The existing STA controller,
Selected/Entire scope, native FeatureId and phase/Punch/Holder/segment binding,
cancel, refresh and stale-result behavior remain present.

## Single conversion and bounded result reading

`BatchViewModelConverter.Convert` returns one projection containing summary,
Part/Bend/tool rows and measured stage durations. The Form consumes that object
once. The compatibility wrappers `FromSnapshot` and `PartsFromSnapshot` remain
available, but the Form no longer calls them separately. Candidate lookups are
indexed by native FeatureId and tool ID instead of repeatedly scanning lists.

The expensive 120/25 fingerprint was caused by repeated whole-Assembly
freshness audits from every nested result getter. Multiplying those audits by
every phase, pair, witness and occurrence dominates result extraction.

`AssemblyBatchSnapshot.ReadSnapshot` now creates a detached, synchronous display
or export projection. A complete input-graph audit brackets the copy. Within
that private copy, result getters still run the shared Assembly/catalog/source
leases and their local sheet/tool/plan checks. The exit audit checks all plans,
setups, review/catalog and model dependencies again. If any input has expired,
the complete projection is discarded, including rows copied before the change.
Source envelopes use the matching model lease; mismatched envelope models are
already rejected when compiling the Assembly geometry bundle.

The scope is thread-local, synchronous and restored in `finally`. Other threads
and retained result getters continue to perform full audits. There is no
cross-run freshness cache, disabled hash validation, worker COM call, or new
numeric proof. Consumers recheck `Current` before publication. The controller
checks again after rendering and keeps polling native freshness on STA.

The copied values are point-in-time display data, never manufacturing
certificates. Missing setup, process, geometry or physical evidence remains
UNKNOWN. No angle, radius, bend, native ID, CLEAR or manufacturing PASS is
invented.

## Timings

`BendAssemblyBatchTimings` records native capture, worker wall time, numeric
kernel time, snapshot validation, snapshot copying, ViewModel conversion, grid
population/paint and Run-to-visible wall time. The Form finishes pending grid
paints before recording the first visible result and exposes the measurements
in diagnostics. Worker kernel durations can overlap across workers and must
not be added to wall time or subtracted to infer COM time.

Native inventory, file/catalog validation, scheduling and publication remain
inside Run-to-visible time; that total is deliberately not represented as the
sum of the selected stages. An atomic SOLIDWORKS capture can still take seconds.
No COM work is moved to a managed worker to reduce that cost.

Row population suspends layout and suppresses redundant selection callbacks.
The actual Part, Bend and tool selection handlers remain active afterward.
`BendCheckForm` retains the child controller explicitly, which also allows the
native regression to exercise the real launcher instead of constructing only
an isolated batch window.

## Changed files after the merge

| Files | Purpose |
| --- | --- |
| `ADDIN.csproj` | Include timing DTO; preserve both project baselines |
| `Commands/BendAssembly/AssemblyBatchCoordinator.cs` | Attach bounded read guard to restored results and snapshots |
| `Commands/BendAssembly/AssemblyBatchReadSnapshot.cs` | Bracketed freshness audits and detached result DTOs |
| `UI/BendAssemblyBatchModels.cs` | One projection and indexed native ID/tool binding |
| `UI/BendAssemblyBatchForm.cs` | Reuse projection, stage measurement, suppress repeated binding callbacks |
| `UI/BendAssemblyBatchController.cs` | Run-to-visible timings and final current check |
| `UI/BendAssemblyBatchTimings.cs` | Measured stage DTO |
| `UI/BendCheckForm.cs` | Explicit child controller ownership |
| `Tests/BendToolProfiles/AssemblyReadTests.cs` | Strict/bounded equivalence, invalidation, thread isolation, exception cleanup |
| `Tests/BendToolProfiles/AssemblyBatchBenchmark.cs` | Measure bounded semantic extraction separately from solver |
| `Tests/BendToolProfiles/AssemblyBatchTests.cs` | Register read regressions |
| `Tests/BendToolProfiles/AssemblyUiBenchmark.cs` | Render real controlled numeric results; close during an active numeric worker |
| `Tests/BendToolProfiles/BendAssemblyUiTests.cs` | Single-conversion/timing regression; retain all converter and Form lifecycle tests |
| `Tests/BendToolProfiles/NativeAssemblyBatchUiValidation.cs` | Real launcher, visible Part/Bend/18-tool selection, rebuild/stale lifecycle, actual capture close, diagnostics and timings |
| `Tests/BendToolProfiles/NativeAssemblyLargeUiValidation.cs` | Real 120/25 cold/warm Live rendering and capture-hit checks |
| `Tests/BendToolProfiles/NativeAssemblyBatchValidation.cs` | Optional large native UI regression and private per-capture/incomplete-run diagnostics |
| `Tests/BendToolProfiles/{ToolProfiles.Tests,BendAssemblyUi.Tests,NativeAssemblyBatch.Validation}.csproj` | Keep and compile all corresponding regressions |

Validation, measured benchmarks, failed attempts and acceptance gaps are in
[Validation-UnifiedIntegration.md](Validation-UnifiedIntegration.md).

## Reproduction

Use .NET Framework 4.8, an x64 MSBuild toolchain and locally installed SOLIDWORKS
interop assemblies. Put outputs and intermediate directories outside the Git
checkout. Disable the existing kill/register build events when validating an
isolated DLL; do not interrupt the user's SOLIDWORKS process.

```powershell
MSBuild ADDIN.csproj /p:Configuration=Debug /p:Platform=x64 /p:KillSolidWorksOnBuild=false /p:RegisterForComInterop=false /p:PreBuildEvent= /p:PostBuildEvent=
MSBuild ADDIN.csproj /p:Configuration=Release /p:Platform=x64 /p:KillSolidWorksOnBuild=false /p:RegisterForComInterop=false /p:PreBuildEvent= /p:PostBuildEvent=
MSBuild Tests/BendToolProfiles/ToolProfiles.Tests.csproj /p:Configuration=Release /p:Platform=x64
MSBuild Tests/BendToolProfiles/BendAssemblyUi.Tests.csproj /p:Configuration=Release /p:Platform=x64
```

Run the managed executable with no arguments, `--assembly-tests`,
`--process-tests`, `--assembly-benchmark <private-output>` and
`--process-reference <private-output>`. Run `BendAssemblyUi.Tests.exe` with no
arguments, `--render <private-output>` and `--benchmark-ui <private-output>`.
The planning project and independent Python oracles retain their own existing
commands. Never publish the private native inputs/outputs when reproducing.

Native `NativeAssemblyBatch.Validation.exe` accepts DLL, new private output,
sheet seed, seed length dimension, Assembly template and local Min workbook.
`--ui` instead accepts DLL, output, six-bend seed, Assembly template, Min workbook
and cut-plane seed. The default copied six-bend seed case is retained.
`ADDIN_NATIVE_UI_GENERATED_SIX=1` adds an experimental diagnostic mode requesting
a six-corner native base-flange test Part from the local template. Its captured
bend count must independently equal six; the current Release diagnostic failed
that check, so this is not a validated replacement fixture. It is not Preview
data. `ADDIN_NATIVE_UI_CLOSE_ONLY=1` instead runs only the isolated three-bend
native capture-close case and records that narrow scope explicitly. The report
records which fixture mode ran. `ADDIN_NATIVE_UI_120=1` enables the large Form regression;
`ADDIN_NATIVE_CAPTURE_DIAGNOSTICS=1` retains first-chance dictionary exception
stacks locally. `ADDIN_NATIVE_FIXTURE_SOURCE` optionally clones a verified
120/25 fixture and verifies all references are inside its new output before
saving copies. Local tool/review paths are supplied only by the operator's
existing private environment configuration.
