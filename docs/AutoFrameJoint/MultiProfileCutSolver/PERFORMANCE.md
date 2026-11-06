# Performance evidence

## Measured copy-harness runs

| Run | Open | Solver | Native apply | Close |
| --- | ---: | ---: | ---: | --- |
| 2354 end-section writer, run 02 | 1067 ms | 40483 ms | 44404 ms | NOT_AVAILABLE in that export |
| 2354 cross-end experiment, run 02 | 1032 ms | 36777 ms | 48445 ms | NOT_AVAILABLE in that export |
| 1618 fr3 × fr4 geometry attempt 03 | 1282 ms | 21349 ms, refused | Native writer not called | 96 ms |
| 1618 fr3 × fr4 stock-copy geometry 01 | 1275 ms | 43237 ms, passed | Native writer not called | 509 ms |
| 2354 cross-end Undo qualification 01, overall FAIL | 1252 ms | 29289 ms | 49861 ms, initial two Cuts passed | NOT_AVAILABLE |
| 1618 fr3 × fr4 stock-copy native writer 01 | 1313 ms | 35913 ms | 57463 ms | NOT_AVAILABLE |
| 3615 T1 stock, legacy writer | 1108 ms | 19893 ms | 26446 ms | NOT_AVAILABLE |
| 3615 T2 shared-Part refusal | 1069 ms | 21030 ms | 273 ms, refused | NOT_AVAILABLE |
| 1618 fr1 × fr2 first stock attempt | 1231 ms | NOT_AVAILABLE, failed support guard | Writer not called | NOT_AVAILABLE |
| Reversed 2354 positive | 1128 ms | 30914 ms | 52457 ms | NOT_AVAILABLE |
| Reversed 2354 injected failure/rollback | 1148 ms | 31127 ms | 51212 ms | NOT_AVAILABLE |
| Subsequent stale-ticket case, native session fault | 1151 ms | Not called | Not called | NOT_AVAILABLE |
| 1618 fr1 × fr2 boundary-witness stock writer | 2693 ms | 36614 ms | 37336 ms | NOT_AVAILABLE |
| 3615 T2 positive attempt, shared-Part guard | 993 ms | 26338 ms | 275 ms, refused | NOT_AVAILABLE |
| 2354 fr04 × fr03 final legacy regression | 1077 ms | 20154 ms | 24761 ms | NOT_AVAILABLE |
| Fresh 2354 stale-ticket refusal | 1138 ms | 35487 ms | 27 ms, rejected before writer | NOT_AVAILABLE |

These are native copy-harness timings, not installed Add-in Preview performance. Cross-reference modification/rebuild phases after the initial apply were not included in the apply timer. Do not sum the columns into an asserted complete interaction time.

**The current 2354 solver/apply durations are too long to describe as smooth Preview.** Passing geometry does not establish acceptable interaction latency.

## Real public SessionManager path on native API

The compiled-source native harness called the actual public SessionManager/Preview classes for `public2354_folded_boundary_01`; this was not an installed UI/DLL test.

| Request | Total API call | Cache/source behavior | Display behavior |
| --- | ---: | --- | --- |
| Initial P1 | 38260 ms | MISS; one 37369 ms public solver call | KEEP Display3 succeeded |
| Unchanged P1 | 287 ms | HIT; no solver call | No Hide or Display3 |
| P2 | 739 ms | HIT; no solver call | Reused KEEP, displayed CUT bodies |
| Return P1 | 429 ms | HIT; no solver call | Kept KEEP, hid only CUT bodies |

The three repeats reused the exact-source cache. Source identity/stamp checks still took approximately 180–205 ms per call. Native Display3 returned status 0, but actual visual visibility is **UNVERIFIED** because no installed UI or screen inspection was performed. Public apply subsequently created two native Cuts with zero body difference; the trace timed the writer at 48873 ms. Closing the public manager succeeded with no remaining Temporary Body.

**The first solve still takes about 37 seconds.** The subsecond repeats establish cache reuse in this run, not acceptable initial Preview latency or smooth installed interaction. An independent 38658 ms solve earlier in this experiment prepared its reference body; it is separate from the public P1 call and must not be reported as a second solver call inside that Preview sequence.

## Existing installed Add-in log

An earlier installed fr04 × fr03 Preview logged 257 ms solve, 26 ms display and 291 ms total Preview. It subsequently created four actual Cuts via native OK. Its 8318 ms AfterClose timer included a blocking result notification and cannot be treated as writer time alone.

A later short startup-only log contains no AFJ command/solver lines. Its process exit code does not establish that the solver caused a freeze or crash.

## Changes under verification

- Per-body face snapshots read plane/cylinder parameters and adjacency once, then perform strip/bend matching in managed arithmetic. They are not reused across mutations or Boolean ownership changes.
- One exact-source rule rejection can skip repeated solver requests for 30 seconds. Reuse requires the existing full API identity/stamp/configuration/transform checks; native/COM/Boolean failures are not cached.
- Invalid new selections invalidate an older successful ticket and clean its Ghost when the bound assembly is active. Failed cleanup retains ownership.
- Apply/session, native writer and notification timers are separate. Successful apply can return its existing result callback without another blocking success modal; failures remain explicitly reported.

These changes need timed native and installed interaction measurements before a latency improvement is claimed.

## Independent timed harness

The geometry-only harness emits `BEGIN` with UTC time before a native phase and `END` with milliseconds from a finally block. It distinguishes activation/ROT attach, copy/hash, OpenDoc6, component inventory, source-body copy, solve, close and final hashes. A long phase can therefore be identified without assigning its cause from total wall time.

The 1618 attempt's 26.863-second case total included a 21.349-second rejected solve. Keep failures in performance analysis; a refusal is not evidence that no expensive work ran.

The 1618 stock-copy run took 52.362 seconds overall, including 5.944 seconds preparing suppressed stock on disposable copies and a 43.237-second solve. It confirms broader geometry behavior, not an acceptable Preview latency.

The 1618 stock-copy native writer pass took 35.913 seconds to solve and 57.463 seconds for the initial native apply. Cross-end Undo qualification later failed independently of its 49.861-second initial apply. Neither run establishes smooth installed Preview performance.

The actual six source files after three exact-baseline class patches passed 89/89 managed contract controls for each branch/local snapshot, including zero interface access inside selection callbacks. These isolated tests neither measure native latency nor prove installed Preview display.
