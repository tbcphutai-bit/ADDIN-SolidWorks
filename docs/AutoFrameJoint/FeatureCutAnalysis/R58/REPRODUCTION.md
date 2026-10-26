# R58 installed API reproduction scope

This describes the sequence that actually passed. It is not a repair instruction for the unresolved main-first opening crash. Local diagnostic executables and raw source remain private because they contain machine paths and CAD inputs; this report publishes only sanitized evidence.

## Binary and copied-document setup

1. Start a separately owned SOLIDWORKS process. Pin its PID and exact start time, attach only its exact ROT entry, and require a blank host. Do not reuse or modify the user's session.
2. Hold read locks on the six source fixture files and record expected hashes. Create a fresh complete set of five Part copies and one Assembly copy. Require every open document to remain inside that copied manifest.
3. For the successful load-order experiment, require the main Add-in to be absent while opening all five copied Parts and then the copied Assembly through `OpenDoc6` with `Silent | OverrideDefaultLoadLightweight`. Check each returned document/path and reject Read-Only/null results. Activate the copied Assembly with `swDontRebuildActiveDoc` and capture a bounded native baseline inventory.
4. Load the **existing deployed** main DLL with `ISldWorks.LoadAddIn`; do not rebuild, deploy, replace or load a clone assembly. Require SHA256 `F22D57943B23D6FF17B6A17EB68E95424A604EAB9F082C180B8162A034FB44C7` and MVID `472fc9a6-5298-4ceb-a696-86bc9a0658fe`. Verify the exported Add-in identity and actual already-loaded managed instance.
5. Load the standalone diagnostic `ISwAddin` bridge under its separate temporary registration. Its connect/automation callback only posts a managed request. All COM reads and modeling then execute on the original native owner STA, with matching process identity, frame thread and `DefaultDomain`. No second main Add-in assembly or duplicate production command is created.

The earlier VSTA route ran in `testconsole.exe` and was rejected before modeling. A diagnostic automation RCW could also enter on a CLR RPC worker. The diagnostic bridge was corrected to transfer only a managed request with `Control.BeginInvoke`; it does not query COM or model on that RPC entry. This is a diagnostic-tool correction, **not a change to the main Add-in or its `AfterClose` implementation**.

## Actual installed command flow

1. Use the existing `AutoFrameJointCommandEntry.Open` and existing native A/B selection boxes. Select the copied components through `Component2.Select4` with marks A=1 and B=2. Wait for the installed deferred selection validation to report the exact pair ready.
2. Invoke the existing `OnButtonPress(107)` handler to request P1. This is an API-simulated button trigger, not a physical button click. Require the existing Preview solution/source ticket to be current; do not replace private selection or eligibility state.
3. Call real native `PropertyManagerPage2.Close(true)`. Do not call `OnClose` or `AfterClose` directly. Require the installed trace to record `APPLY_VERIFIED cuts=4 ... saveApiCalled=False`, require actual `AFTER_CLOSE_END`, and require native close to return.
4. While bridge work is pending, the parent reads progress files and the owned process only. It makes **no concurrent SOLIDWORKS COM calls**. A successful arm/load return is not a test PASS. Resume parent COM checks only after a completed successful bridge result.
5. Independently inspect added native Features, Sketch state/supports, final Body health, closest gap and Boolean intersection. The first corner created four actual Cuts and four fully constrained face-supported Sketches, with zero new planes.

## Four corners on the same in-memory copies

Continue without closing/reopening CAD or reloading the main Add-in. The first successful corner is retained; then execute the same installed command flow for the other three ordered pairs:

| Corner | A | B | New native Cuts | New Sketches |
|---:|---|---|---:|---:|
| 1 | fr01 | fr04 | 4 | 4 |
| 2 | fr04 | fr03 | 4 | 4 |
| 3 | fr03 | fr02 | 4 | 4 |
| 4 | fr02 | fr01 | 4 | 4 |

After the last corner, recheck all 16 native Sketches and all four final pair gaps/intersections. Creation-time support checks establish an existing Part `Face2` before that corner. The final recheck establishes native `Face2`, reference type 2, constraint status 3 and zero dangling/overdefining relations. It does not establish that a single persistent face identity remained unchanged from before the whole four-corner loop.

The authoritative cumulative result is **16 generated native Cuts, 16 Sketches, zero new planes**. Every final pair retained an approximately 0.10 mm gap, zero overlap and intersection code 0. The historical authored inventory remains separate at 43 reference Cuts and 42 reference before/after cases.

## Cleanup and evidence limits

After verification, close only the owned copied documents through API `CloseDoc` without saving, verify the host has no documents or running command, and call `ExitApp`. The partial server-fault host and the successful four-corner host both exited with code 0. Neither process was killed. Final six-source/six-copy hashes remained unchanged for both cleanup scopes.

The 16 generated Cuts existed in the disposable in-memory models and were discarded on no-save close. No CAD with these generated features was published. Original CAD and the deployed main source/DLL/PDB remained unchanged.

The test qualifies the actual installed callback chain under **CAD-first Add-in loading**. It does not qualify main-first startup, visible Preview, smoothness, broad profile-family coverage, flat-pattern correctness, or authored manufacturing equivalence. No computer-use automation or visual inspection was used.
