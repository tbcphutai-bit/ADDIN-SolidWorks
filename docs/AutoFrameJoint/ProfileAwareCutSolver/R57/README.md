# R57: installed crash and diagnostic tracing

The installed R56 Apply test **crashed**. This delivery adds diagnostic traces, not a crash fix. See [crash evidence](CRASH_ANALYSIS.md).

The diagnostic source SHA256 is `96174f1f6d97bc3f541900bd2534025741e9c55f7c3d6622b629080c790da41e`. It is copied into the existing Ghost source class; the previous source is backed up locally. Actual six-source detached compilation passed. The Add-in project was not built or deployed, and DLL/PDB/project/UI/other command files remain unchanged.

No Cut Plan, Boolean, selection, callback scheduling or native factory arguments were changed. The existing factory result is still registered before any new post-factory trace. There was no installed runtime retest of R57, and no geometry or crash-fix PASS is claimed.

## Trace boundaries

`[AFJ-R57-BOUNDARY]` records UTC time, managed thread and `BEGIN` / `END` / managed `FAULT` around:

- The exact owned-Sketch mapping and target Part identity check.
- Native document activation context, ActiveDoc identity, title and `ActivateDoc3`.
- Fresh Feature lookup, Sketch interface/constraint status and model-to-Sketch transform/inverse.
- Alignment calculations and the Assembly/Part factory checkpoints.
- Selection clear, exact profile selection and `FeatureCut4` entry.

`RETURN_REGISTERED` is emitted only after the returned Cut has been captured by the existing ownership journal. A fatal native heap error may bypass managed exception handlers; a final unmatched `BEGIN` can narrow the detection boundary but does not identify the original memory corruptor.

## Manual diagnostic run

1. End the previous debug/SOLIDWORKS session, reload the externally edited source, and manually rebuild through the existing project workflow.
2. Confirm the newly loaded DLL emits `[AFJ-R57]` with `diagnostic=BOUNDARY_TRACE_ONLY` and its new MVID. Editing source alone does not replace the loaded DLL.
3. Reproduce only the affected pair on a disposable Assembly/Part copy. Retain the complete Output log, including the last R57 boundary lines and process-exit code.

The existing installed failure remains **unresolved** until its native cause is established and a correction passes an installed Add-in retest. Windows WER evidence was retained locally; no raw logs, dumps, CAD, private paths or customer information are published here.
