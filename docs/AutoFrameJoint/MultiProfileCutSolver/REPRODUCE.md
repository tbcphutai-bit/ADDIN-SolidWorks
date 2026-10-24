# Reproducing evidence

The files in `evidence/` are standalone diagnostic sources, excluded from the Add-in project. Compile only a detached console EXE with the installed SOLIDWORKS interop assemblies and standard .NET Framework references. Do not build or deploy the Add-in as part of this procedure.

## Required safeguards

- Use a new, empty SOLIDWORKS process. Record its exact PID/version. `PID_ROT_CONNECTION` binds that PID and never silently falls back to another instance.
- Use a private disposable pair dataset whose Assembly references stay inside its private folder. Each run must use a fresh output directory. Do not publish generated CAD copies or full private logs.
- `AFJ_REQUIRE_BLANK_SESSION=1` and `AFJ_R51_SW_PID=<owned test PID>` must be set. Leave `AFJ_CREATE_TEST_SW` unset: COM activation can reuse an existing process.
- Read the native driver before executing it. Stock tests suppress existing Cuts only inside contained disposable copies. No tested driver calls a Save API.
- Independent diagnostic EXEs must compile against the six AFJ source files from the source commit identified in `SOURCE_VERIFICATION.json`; no project/output/deployment change is needed.
- Arguments: fresh output directory, private pair directory, A occurrence suffix, B occurrence suffix, case name. Native Cut cases include `positive`, `rollback_after_both_parts`, `stale_preview_rejected`, `public_preview_then_apply`, `unsupported_current`, and `shared_part_rejected`.
- The public Preview harness runs P1/P1/P2/P1 through the existing manager. A successful Display3 API return is not a visual or installed UI PASS.

## Research helpers

`DIRECT_COINCIDENT_FOLLOW_RESEARCH` demonstrates a point related directly to its own Edge and the other Frame Face. It temporarily suppresses mates only on the contained copy to qualify a real 1 mm placement change, then restores every recorded state. It creates no Cut.

`CROSS_END_UNDO_FAILED_RESEARCH` and its adapter reproduce an experiment whose Assembly-owned recording did not finish. They are diagnostic evidence of a failed qualification, not production integration instructions. The production source does not call this adapter.

`MANAGED_89_CONTRACTS` uses interface proxies and is not SOLIDWORKS/native UI evidence. Its `C:\copies\...` strings are synthetic path-normalization fixtures, not private machine paths. Set `AFJ_PREVIEW_CONTROL_SOURCE` to the actual directory containing the three command classes before running those controls.

The report's native timings are cross-process COM measurements. They do not establish installed Add-in latency or visible Preview smoothness.

## Hash conventions

`PUBLICATION_INVENTORY.json` hashes report/evidence text after removing any UTF-8 BOM and normalizing CRLF to LF, so Git checkout line endings do not change verification. Source/protected-binary hashes in `SOURCE_VERIFICATION.json` describe exact local file bytes measured at the audit, not Git blob hashes.
