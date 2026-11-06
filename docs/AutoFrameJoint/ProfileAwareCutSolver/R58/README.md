# R58 — installed crash investigation and API reproduction

**Four-corner installed API result: PASS. Crash root cause: OPEN / NOT FIXED.** All four corners completed through the actual deployed Add-in on the same in-memory disposable copies: **16 new Cuts, 16 fully constrained 2D Sketches, zero new planes**. The successful host opened copied CAD before loading the deployed Add-in. Earlier failures with the main Add-in loaded before CAD remain unresolved. Visible Preview and smoothness are unverified.

The user-built DLL has SHA256 `F22D57943B23D6FF17B6A17EB68E95424A604EAB9F082C180B8162A034FB44C7` and MVID `472fc9a6-5298-4ceb-a696-86bc9a0658fe`. The installed log contains R57 boundary markers, so the earlier diagnostic source reached the running Add-in. R57 added diagnostics; it was not a confirmed crash repair.

The first `FeatureCut4` returned a native Feature with ID 735 and the transaction registered it. The last marker is `U_JOURNAL.READ_BEGIN_A`; there is no matching `READ_END_A` or completed native Cut verification. SOLIDWORKS then exited with `0xC0000374`. Windows recorded the failure in `ntdll.dll`. This identifies the detection window, not the operation that originally corrupted memory.

R58 identity checks passed three times with the same deployed SHA/MVID, `SLDWORKS.exe`, `DefaultDomain`, and the native main STA thread. The first full attempt was rejected by the diagnostic automation entry's STA precondition before opening AFJ. After the six disposable documents were closed without saving, the next fixture-opening cycle crashed during `OpenDoc6` for its third copied Part. A separate fresh-host attempt returned `0x80010105` (`RPC_E_SERVERFAULT`) during its second copied-Part open; that process remained alive and no matching native-crash event was found. Both failures happened before Preview or Cut. Whether they share an original corruptor with the installed user crash remains unverified.

Run 10 then proved the exact same DLL and Add-in instance in a fresh SOLIDWORKS process, `DefaultDomain`, on the native main STA thread. Existing selection/Preview code ran, real native `PropertyManagerPage2.Close(true)` invoked the installed `AfterClose`/Apply chain, `APPLY_VERIFIED cuts=4` and `AFTER_CLOSE_END` were observed, and native close returned. Independent parent API checks found four new Cuts, four fully constrained 2D Sketches on original `Face2` supports, zero new planes, healthy solids, a measured minimum gap of `0.09999999999976694 mm`, and zero overlap. The Preview button handler was API-simulated; actual viewport visibility and smoothness remain unverified.

The remaining three corners then completed in the same process and on the same unsaved copies, without reopening CAD or reloading the main Add-in. Ordered pairs were `fr01 × fr04`, `fr04 × fr03`, `fr03 × fr02`, and `fr02 × fr01`. Every corner invoked real native `Close(true)`, observed installed Apply verification and `AFTER_CLOSE_END`, and returned successfully. Final API rechecks covered all 16 Sketches and all four pair gaps after the last corner. All Sketches retained status 3, native `Face2` supports and zero dangling/overdefining relations; all four gaps were approximately 0.10 mm with zero overlap and intersection code 0.

This is a real installed-flow PASS for all four corners under the recorded load order. It is not a main-source crash fix or a qualification of main-first startup. The **16 generated native Cuts** are separate from the historical **43 authored reference Cuts / 42 before-after reference Cuts** and are not added to those reference counts.

See [FOUR_CORNERS_API.json](FOUR_CORNERS_API.json) and [FOUR_CORNERS_API.csv](FOUR_CORNERS_API.csv) for per-corner results, final Sketch rechecks and sanitized native event proof. [REPRODUCTION.md](REPRODUCTION.md) records the actual successful load order, existing command flow and cleanup. [CRASH_ANALYSIS.md](CRASH_ANALYSIS.md), [TEST_MATRIX.md](TEST_MATRIX.md), [TEST_MATRIX.json](TEST_MATRIX.json), and [TEST_MATRIX.csv](TEST_MATRIX.csv) preserve the earlier failures and qualification limits.

## Qualification boundaries

- `API_VERIFIED`: a value or result actually read through SOLIDWORKS API in the named test scope. A returned factory Feature alone does not qualify the Cut as geometrically verified.
- `GEOMETRY_VERIFIED`: independent native B-rep checks completed in the named test scope.
- `INFERRED`: an explanation or rule that has not been directly verified.
- Installed debug-trace and Windows-event observations are identified as `LOG_OBSERVED`; this describes the source of evidence and does not promote a Cut or geometry result to `API_VERIFIED` or `GEOMETRY_VERIFIED`.

The recorded external-process regression passes remain historical. They did not execute the installed PropertyManager native close callback and cannot establish that this crash is fixed.

## Project protection

No main Add-in Build, Rebuild, F5, deployment, project/output change, or merge is part of this investigation. Standalone diagnostic tools are compiled separately. Native modeling is permitted only on a complete disposable copy. Original/copy hashes remained unchanged after runs 07, 08, 09, 10 and the final four-corner continuation; run-08 hashes were checked independently because the parent audit was interrupted. The final disk audit completed with unchanged original, copy and deployed binary hashes and no pending macro. Main source, DLL and PDB remain unchanged.

Cleanup also completed for the partial server-fault host and the successful four-corner host: copied documents were closed with API `CloseDoc` without saving, the blank host was verified, and `ExitApp` returned process exit code 0. No process was killed. All six source files and all six copies retained their expected hashes after each exit. The 16 generated Cuts were verified in memory and discarded on no-save close; no saved generated CAD is claimed.

For all four corners, creation-time support checks refer to an existing Part `Face2` before its corner. Final rechecks establish native `Face2` supports and valid constraints; they do not prove one persistent original face identity across the entire loop.

This public report contains no CAD, serialized B-rep, persistence-reference bytes, original debug log, binary/PDB, customer identifier, or private machine path.
