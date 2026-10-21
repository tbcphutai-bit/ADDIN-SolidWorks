# Check Chấn — live Run/source diagnostics and bounded acceptance

Date: 2026-10-21. Continuing PR #37 from
`da74ece84c5d0a0256b1f1825106abb8bd5a3fbf`; no new PR or merge.

## Confirmed findings and fixes

1. `BendAssemblyBatchForm.RenderSummary` replaced operational diagnostics with
   a generic missing-process-evidence message whenever technical detail was off.
   An early failed Run could therefore show 0%/0 Parts while its actual failure
   was obscured. Empty/failed runs now retain the real diagnostic/instruction.
   Controller failures also retain exception type/message/stack and local log path.
2. Native Assembly fixture was rejected **before capture**. The initially
   opened fixture needed rebuild. The controlled copy confirmed
   `NeedsRebuild=1`, no active sketch, and edit target equal to the root;
   rebuilding only the private copy changed that value to 0. The safety gate is
   retained, with distinct Vietnamese messages for inactive host/configuration,
   rebuild/sketch state, and in-context component editing. No automatic rebuild
   was added to the product.
3. Tool source loading is now observable in the actual SOLIDWORKS process:
   settings path/existence, effective configuration, only the five explicitly
   whitelisted ADDIN source overrides, read outcome, ToolCount/DataCount,
   matched CAD pairs and independent clamp-verification count. No CAD coordinates,
   file contents, arbitrary environment or secrets are logged.

The old PID 32556 report had DataCount=0. This was **not reproduced** in the
fresh native sessions. The final in-process capture read the existing
`%LOCALAPPDATA%/ADDIN/bend-tool-dxf-source.json`, with no source overrides,
`UseMasterPairsAsLibrary=true`, ToolCount=18, DataCount=18, MatchedPairs=18,
Error=null, ClampVerified=0. Do not claim the unavailable old process's exact
configuration discrepancy has been diagnosed or repaired. The loader's precedence
and CAD selection were not changed speculatively.

## Scope of changes

- `Commands/BendToolProfiles/ToolProfileLibrary.cs`: bounded local JSONL/Trace
  logging and settings/provider instrumentation; source precedence unchanged.
- `Commands/BendCheckSession.PartBatch.cs`, `.AssemblyBatch.cs`: native capture,
  worker submission, completion/error diagnostics; precise Assembly preflight.
- `UI/BendAssemblyBatchForm.cs`: Click/bind stages and operational-error retention.
- `UI/BendAssemblyBatchController.cs`: event, Run, minimum read, Begin, Pump,
  completion, cancellation and exception diagnostics.
- `UI/BendAssemblyBatchLiveBackend.cs`: Part/Assembly backend entry diagnostics.
- `UI/BendCheckDisplayText.cs`: Vietnamese source/mapping/evidence reasons.
- Targeted UI/source regression and opt-in bounded live acceptance in the existing
  test bridge. Existing acceptance modes are unchanged when the flag is absent.

No Check Min math, collision kernel, 18-pair CAD mapping, Designer, unrelated
command or production startup setting changed. The actual Visual Studio source
is `C:/Tao moi/ADDIN/ADDIN.csproj`; Debug/x64 still outputs `C:/Tao moi/ADDIN.dll`.
Source working-tree integration branch remains
`integrate/check-chan-pr37-vs-20261021`; unrelated local edits were preserved.

## Builds and managed regression

Debug/x64 product build: **PASS**, final installed path above. Pre/post build
events and COM registration were disabled for this build. The original DLL/PDB
and the first instrumented DLL/PDB were backed up separately before replacement,
after confirming no SOLIDWORKS process remained.

Final DLL SHA256:
`ce06c70e3971ad946d67eea3e757d3db73247065c33c09d7bc6bbf3f7b81cd17`

MVID: `7ec46cff-27fc-4a9a-aa2a-14657fcefce1`.

Managed UI/source regression: **30 groups, 198 assertions, 0 failures**.
Includes whitelist privacy, explicit master-mode override behavior, preservation
of a real failed-run message with technical detail off, a visible recognized Part
and 18 UNKNOWN candidate slots with missing geometry, existing controller
cancel/close boundaries, and independent Check Min projection.

Release build: **NOT RUN** in this narrowly requested Debug delivery.
33-Part/120-25 benchmarks and broad native regression: **NOT RUN**.

## Native execution — exact final DLL

Owned SOLIDWORKS PID **29112**, in-process host STA. The actual existing
Check Chấn command opens the Form, and the actual Run button invokes
`OnRunBatchClicked -> RunBatchRequested -> RunAsync -> ReadMinimumAsync ->
BeginAssemblyBatch -> Pump -> Completion -> BindSnapshot`.
Geometry/file computations use managed DTOs on workers; native calls stay on STA.

| Existing fixture / action | Execution | Visible Parts | Bends | 18-tool candidates | Click-to-run-end |
| --- | --- | ---: | ---: | ---: | ---: |
| XL-02 Cold | PASS | 1 | 2 | 36 | 1.449 s |
| XL-02 Refresh | PASS | 1 | 2 | 36 | 0.787 s |
| Same small Assembly, Entire | PASS | 3 | 9 | 162 | 1.451 s |
| Same small Assembly, Selected | PASS | 1 | 6 | 108 | 1.075 s |

XL-02 native FeatureIds 53/59 and both `MeetsMinimum` results were retained.
The Assembly includes zero-/three-/six-bend fixtures: Check Min results remain
mixed (insufficient material/input data, below-minimum, grooved-accepted), not
reported as all PASS. The zero-bend member remains visible without invented bends.

ToolCount/DataCount/MatchedPairs = **18/18/18** in each successful live result.
Punch and Holder remain **UNKNOWN** without verified setup evidence. Numeric
collision queries = **0**, not a numeric-collision acceptance claim.

Cancel/close during Run: **PASS**. Idle close: **PASS**.
Disconnect/detach: **PASS**. ExitApp: **PASS**, exit code **0**.
Protected original CAD/Excel/config files: **16 checked, 0 changed**.
Only an explicitly guarded private Assembly fixture copy was rebuilt in memory;
no save or rebuild was performed on the original Assembly to force acceptance.

The initial diagnostic PID **1844** is retained as **Part PASS / Assembly FAIL**
because the Assembly precondition had not been met; shutdown was PASS.
There was one subsequent verification after the confirmed readiness issue and
message fix. No repeated search for a lucky PASS, new Part generator, expanded
benchmark or additional Assembly was used.

## Local diagnostics and remaining limits

Default local trace is `%LOCALAPPDATA%/ADDIN/Logs/CheckChan-<PID>.jsonl`, capped
at 8 MiB per process; the same selected events appear in Trace/Debug Output.
`ADDIN_BEND_DIAGNOSTIC_FILE` may name an explicit absolute private log file.
Raw logs, copied fixtures and coordinate-bearing evidence remain local.

For the bounded bridge mode set `ADDIN_LIVE_DEBUG=1`, an explicit existing
`ADDIN_LIVE_SMALL_ASSEMBLY` copy, and its `ADDIN_LIVE_ASSEMBLY_COPY_ROOT`.
Use the existing in-process native harness with the Part fixture and no full
representative manifest. The copy-root guard permits fixture preparation only;
it does not permit product code to change CAD or infer process states.

This confirms usable Run/binding, source reading and controlled close/shutdown
for these two documents. It does not certify prior heap crashes resolved,
verified physical mounting, holder material roles/length, process geometry,
motion/sequence feasibility or manufacturing PASS. Old Debug DataCount=0 remains
NOT REPRODUCED; the next occurrence now has exact in-process source/flow evidence.
