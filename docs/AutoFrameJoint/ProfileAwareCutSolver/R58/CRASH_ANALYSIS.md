# Installed R57 failure window

## Observed installed sequence

Source: user-supplied debug log, SHA256 `14FBB1271452C7B67D3ED5EA6F705278373046BD1206EBD27754482ED571DBD1`, and matching Windows Application events.

| UTC, 2026-10-25 | Observation | Qualification |
|---|---|---|
| 15:00:23.7937886 | Activation of the first target Part completed. | `LOG_OBSERVED` |
| 15:00:23.8329409 | First `FeatureCut4` began after profile selection and both pre-factory checkpoints completed. | `LOG_OBSERVED` |
| 15:00:24.0587414 | Factory-returned native Feature ID 735 was registered in the transaction. | `LOG_OBSERVED`; provisional ownership only |
| 15:00:24.0626447 | `RETURN_REGISTERED` marker completed. | `LOG_OBSERVED` |
| 15:00:24.2896794 | `U_JOURNAL.READ_BEGIN_A` was written. | Last observed application marker |
| 15:00:25, Windows event | Native heap failure `0xC0000374`, fault module `ntdll.dll`; process terminated. | `LOG_OBSERVED` |

No `READ_END_A`, `CONFIRMED_CLOSED_A735`, or `NATIVE_CUT_VERIFIED` follows. Thus this run has one factory-returned provisional Cut and **zero completed Cut verifications**. It does not demonstrate a valid saved Cut, rollback, or final body.

The earlier R56 installed run reached a different later boundary. The R57 failure after the first A factory shows that the fault is not confined to the A-to-B transition.

## Reader scope and limits

After `READ_BEGIN_A`, the reader performs bounded top-level traversal, flat `FeatureManager.GetFeatures(false)` inventory, native identity reconciliation, original/passive sheet-metal resolution, parent reads, and ownership/type/name/suppression fingerprints. The boundary marker does not identify which individual native call failed. Neither an unsafe call nor the initial corruptor is established by this trace.

`RequireAssemblyContext` checks the Assembly edit target and absence of an active Assembly Sketch. It does **not** activate the Assembly. The log shows Part activation before the factory; a later activation by another callback has not been proved.

The R57 journal contains no explicit `ReleaseComObject`, `FinalReleaseComObject`, forced GC, recursive subfeature traversal, or modeling operation in its passive observation helper. Removing ownership checks or skipping failed inventories is not supported by the current evidence.

## Hypotheses, not conclusions

- Modeling and reading the feature inventory synchronously inside native `AfterClose` differs from the earlier external harness. That difference is an `INFERRED` lifecycle hypothesis. The API permits work in `AfterClose`; the callback is not by itself evidence of a forbidden operation.
- Document-change or regeneration observers may run in the installed host. No specific observer has been proved to corrupt memory.
- The 127 WPF binding warnings precede command opening in this log. They do not establish the native corruption cause.

No rollback or continued modeling is claimed after process death. No original Part or Assembly is declared corrupt from this result.

## R58 diagnostic setup

1. The VSTA macro experiment proved that this installation executes the macro in `testconsole.exe`, outside the SOLIDWORKS process. Its strict process-identity guard rejected it before CAD modeling. It cannot qualify installed callback execution.
2. A standalone diagnostic `ISwAddin` bridge runs its work after the native connect/automation callback returns, verifies the owned process and native STA thread, and requires the exact already-loaded main DLL SHA/MVID and Add-in instance.
3. An initial clean session did not automatically load the main Add-in. Its identity guard rejected the missing assembly before CAD modeling. The resumed parent driver explicitly loaded the existing deployed main DLL through `ISldWorks.LoadAddIn` and verified exported identity. This loads the existing binary; it does not rebuild or deploy it.
4. Full mode uses existing A/B selection and Preview handler 107, then calls real native `PropertyManagerPage2.Close(true)`. Handler 107 is API-simulated; a physical button click and viewport visibility remain unverified.
5. Run 07 verified deployed-DLL identity in the actual SOLIDWORKS process and native main STA thread. All six copied documents opened and the read-only baseline inventory completed. The diagnostic automation entry then rejected the full request because that RPC did not execute on its owner STA. No AFJ command, Preview, or Cut was attempted. The six copies were subsequently closed without saving; original, copy and deployed binary hashes remained unchanged.
6. Run 08 repeated the same-process identity PASS in that host. During the next fixture-opening cycle, only the first two copied Parts reached their `AfterOpen` markers. The host then died during `OpenDoc6` for the third copied Part, before the Assembly or any AFJ PropertyManager command was opened. Windows event 1000 at `2026-10-26T00:58:44.3561089Z` records `0xC0000374`, `ntdll.dll`, offset `0x118095`. This is a reproduced native heap failure in the installed host, not a reproduced AFJ Cut failure.
7. The run-08 parent audit remained `RUNNING` because termination interrupted its exit-code/final-audit path. It must not be read as a test PASS or successful completion. An independent post-crash check confirmed all six originals and all six copies still matched the expected hashes. No post-crash rollback, native body check, or cleanup success is claimed.
8. Run 09 used a fresh SOLIDWORKS process and again passed exact main-DLL identity. Only the first copied Part completed opening. The second `OpenDoc6` returned `0x80010105` (`RPC_E_SERVERFAULT`) before AFJ opening. The process remained alive and no matching Windows native-crash event was found. This is an API server fault, not a demonstrated native process crash. Original, copy and protected binary hashes remained unchanged.
9. Run 10 isolated load order by opening all six copied documents before loading the deployed main Add-in. It verified the exact deployed DLL in the actual SOLIDWORKS process, DefaultDomain and native main STA thread. Existing selection and Preview code ran, followed by real native `PropertyManagerPage2.Close(true)`. The installed Apply reported `APPLY_VERIFIED cuts=4`, `AFTER_CLOSE_END` was observed, and native close returned successfully.
10. Independent parent API checks then found exactly four new Cuts and four 2D Sketches: all four had constraint status 3, zero dangling and overdefining relations, and original `Face2` supports. No new reference plane was found. Native final-solid checks returned zero faults; the measured minimum gap was `9.999999999976694E-05 m`, overlap was zero and the intersection return code was 0. Original, copy and deployed binary hashes were unchanged. These checks qualify final native health/gap/intersection and feature counts; they do not establish authored manufacturing or flat-pattern equivalence.
11. The continuation completed the other three corners in the same installed host, same loaded DLL and same in-memory copies. Ordered pairs were `fr01 × fr04`, `fr04 × fr03`, `fr03 × fr02`, and `fr02 × fr01`. Each corner completed real native `Close(true)`, installed Apply verification, `AFTER_CLOSE_END`, and native return. The final cumulative result was 16 actual new Cuts, 16 new Sketches and zero new planes.
12. Final API rechecks after the fourth corner covered all 16 Sketches: every Sketch retained constraint status 3, native `Face2` reference and zero dangling/overdefining relations. All four final pair gaps were approximately 0.10 mm, overlap was zero and intersection codes were 0. Independent passive-sheet-metal observation and bounded top-repeat lists were empty. The final disk audit completed with original/copy/main-DLL hashes unchanged and no macro pending. Per-corner native event evidence is sanitized in [FOUR_CORNERS_API.json](FOUR_CORNERS_API.json).
13. All four corners are therefore qualified for this installed callback path under the recorded alternate load order. Actual visible Preview and smoothness remain unverified because the button handler was API-simulated and no visual inspection was performed. This is not a fix or a PASS for the earlier main-first startup path.

The run-08 failure before AFJ opening means `AfterClose` cannot explain that individual run. It does not prove the initial corruptor in either run, a defective CAD file, a specific taskpane observer, or a shared cause between the installed user crash and the copy-opening crash.

The successful run-10 native `AfterClose` sequence further prevents treating that callback as inherently invalid. Opening documents before loading the Add-in is an experimental scope difference, not a shipped repair or a conclusion about the startup failure cause.

The private R58 callback-deferral candidate passed detached compilation and managed gate tests. It has not been installed or qualified by native API and is not reported as a crash fix.

The 16 generated Cuts in this reproduction are separate from the historical 43 authored reference Feature Cuts and 42 reference before/after cases. No reference data count or earlier verified result is overwritten.
