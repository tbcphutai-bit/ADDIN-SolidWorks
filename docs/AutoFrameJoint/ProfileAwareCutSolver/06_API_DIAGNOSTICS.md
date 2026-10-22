# API diagnostics and bounded failures

The native extraction previously failed inside the process sandbox. Outside sandbox, the existing SldWorks.Application ROT object is accessible; no additional SOLIDWORKS process was created. Detached executables run x64 STA, use installed SOLIDWORKS 2024 Interop and record exception type, HRESULT, message and stack. No registry/COM registration changes were performed.

Known research issues:

- CreateTrimmedSheet4 needed DispatchWrapper[] for COM curves; a plain object[] produced RPC_E_SERVERFAULT 0x80010105.
- CreateBodyFromBox3 produced DISP_E_ARRAYISLOCKED 0x8002000D. Analytic rectangular sheet extrusion replaced it.
- Untrimmed arc curves caused CreateExtrudedBody RPC_E_SERVERFAULT. Trimming resolved the Boss oracle replay.
- Direct legacy Cut direction-reference getters remain -1/null; effective tool directions verified by body replay are GEOMETRY_VERIFIED, not direct API getter evidence.
- Flat T1 Parts were prepared as folded Default only on separately named private assembly fixtures. Source files retain their hashes.

Runtime Boolean statuses are accepted narrowly: no-error results are validated; disjoint/empty intersection statuses are allowed only for the relevant empty operation. Every nonempty returned solid must pass Check2. Failure/refusal logs include HRESULT and stack where an exception occurred. No Part is called corrupt because an OpenDoc6 result was null.

## Session transition failure — material stability limit

After all eight native-copy geometry/Preview rows had been written, the next harness was started before the previous shell had confirmed process exit. Windows Application Error event 1000 at 2026-10-22 06:53:40 +07:00 records SLDWORKS 32.5.0.48 failing in ucrtbase.dll with exception 0xC0000409. The exact cause is UNVERIFIED; the process-transition overlap is recorded as a possible contributing condition, not a proven root cause. No original Part was saved.

The initial variant harness failed before producing a test row. A bounded STA probe then observed MK_E_UNAVAILABLE 0x800401E3 and no SOLIDWORKS ROT monikers. Its raw HRESULT/stack is retained. A later existing session was successfully attached (UserControl=true; UserControlBackground=true); the one-activation fallback was not used. The variant suite runs sequentially in that session. Session survival and installed Add-in stability are not proven by successful individual Body checks.

The source implements no application launch/shutdown. Recovery belongs to the detached harness. Official [CloseDoc lifecycle documentation](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.isldworks~closedoc.html) also warns that closing the last document can end a background session. That documented normal lifecycle does not explain away the observed crash event. See evidence/API_SESSION_TRANSITION.json.
