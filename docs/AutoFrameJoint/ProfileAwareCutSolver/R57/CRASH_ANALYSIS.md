# R57: installed Add-in crash analysis

**Installed integration result: FAIL.** The new run loaded the rebuilt Add-in with MVID `2ed2cd25-7c9d-4d63-93b4-df4e6215dcd5`; the same module emitted the R56 section-graph/Face2-Sketch marker with plane fallback disabled. The marker establishes the code family in that loaded module; it does not independently prove an exact source SHA.

The selected pair was `2354/fr01` (A) × `2354/fr04` (B). Both selections resolved. Automatic P1 Preview returned success in 488 ms; both temporary bodies returned Display3 status 0. Actual visual appearance is not established by that scalar result.

| Ordered boundary | Observed result |
|---|---|
| OK / OnClose / AfterClose | Valid pair accepted; Apply callback entered after the page closed, managed thread 1 / owner 1 / callback depth 0. |
| Preview cleanup | Both tracked body Hide operations and redraw returned before Apply entry. |
| Native Sketch creation | Four native Sketches reached closed confirmation: B cover, A loser, B relief, A relief. |
| First two Cuts | A loser Cut and A far-Bend clearance Cut each reached `NATIVE_CUT_VERIFIED` and completed following invariants. |
| Next operation | The last completed boundary was `BEFORE_CUT_U_RULE:COVER_WEB_WALL.INVARIANTS_END`, for B. No subsequent B checkpoint read, B Cut verification or Apply completion appears. |
| Process termination | SOLIDWORKS exited with `0xC0000374`; the last timestamped trace was `2026-10-25T06:48:26.6286201Z`. The fatal exit line itself is not timestamped. |

Windows Application Error event 1000 at `2026-10-25T06:48:27.8564577Z` independently records SOLIDWORKS `32.5.0.48`, exception `0xC0000374`, fault module `ntdll.dll` `10.0.26100.9711`, offset `0x118095`, process `0x572C` (22316). Windows Error Reporting event 1001 at `06:48:38.8400328Z` records the corresponding APPCRASH / `PCH_40_FROM_ntdll+0x118095` signature. These establish the native heap-corruption detection/termination signature. They do not identify the original corrupting component or instruction; naming ntdll.dll as the detector is not a demonstrated root cause.

The log narrows the failure window but does not identify the exact native call. In particular, it does not prove that B's Cut factory was entered. Heap corruption can be detected after its original cause; the last successful invariant is not the corrupting instruction. No native stack or dump is available in this evidence package.

There were 127 WPF binding errors: 95 during startup and 32 at the first native-Cut transition, including five missing `HasPart` bindings on `TaskPaneViewModel`. No further binding burst appears after A's second Cut. These messages demonstrate binding failures/task-pane activity, not a proven cause of native heap corruption. No further R52 callback is logged after Apply entry; unlogged native or document-change events are not excluded. Mirrored transaction/Apply trace lines do not establish duplicate Cut execution.

The earlier 20 native Cuts / 20 fully constrained Face2 Sketches across five pairs passed a standalone API harness. That evidence remains valid within its stated scope. It does **not** establish installed Add-in integration, callback lifetime, UI behavior or freedom from this crash. The installed failure supersedes any suggestion that harness PASS qualified those behaviors.

R57 changes, if supplied with this analysis, are diagnostic boundary tracing only. They are not a claimed crash fix, geometry change or successful installed retest. Root cause and correction remain **UNVERIFIED**.
