> **Correction after successful retry:** the earlier report below describes an in-sandbox probe. Outside sandbox the same machine exposed running SOLIDWORKS processes and an existing COM session (revision 32.5.0); all 15 Parts and 43 Cut-Extrude nodes were read. The prior different-session hypothesis is superseded by the observed sandbox restriction. Current status and remaining semantic gaps are in API_EXTRACTION_SUMMARY.json and 07_UNVERIFIED_FINDINGS.md.

# SOLIDWORKS API Access Diagnostics

**Status: blocked at COM session discovery; no FeatureManager extraction was performed.** This is an environment/session access failure, not evidence that any Part is corrupt.

## Result

The user confirmed SOLIDWORKS was open. From the Codex command executor, however, `Get-Process SLDWORKS` returned no visible process and an STA `GetActiveObject` call for `SldWorks.Application` could not find a Running Object Table entry. It failed with `MK_E_UNAVAILABLE` (`0x800401E3`). The PowerShell wrapper reported `0x80131501`; the nested COM exception and stack are recorded in `API_ACCESS_DIAGNOSTICS.json`.

This means the current executor cannot attach to the user's visible SOLIDWORKS session. The evidence is consistent with a different Windows logon/session or an isolated command-execution context. It does not prove which one, because process/session inspection is restricted in this executor. No additional SOLIDWORKS instance was started.

## Checks performed

- The command host was PowerShell 7.6.5 in STA, running as a standard user (`IsInRole(Administrator) = false`); its interactive `SESSIONNAME` was empty.
- The SOLIDWORKS executable registered for the COM class had file version `32.5.0.0048`. Registry entries for SOLIDWORKS 2024 and 2025 were present; mapping the executable build to a marketing release was not independently verified.
- `SldWorks.Application` was registered with CLSID `{AFBEC3B2-B1A6-4908-B608-D97D2AAB5498}`.
- The exact sample Part was present locally and readable by the current user. The filesystem read check does not count as opening it in SOLIDWORKS.
- Process enumeration returned no visible `SLDWORKS.exe`. `tasklist` could not authenticate in this executor, and CIM process inspection was denied/unavailable.
- The earlier attempt reported `OpenDoc6` returning null with error/warning `0/0`; earlier COM activation diagnostics included `E_ACCESSDENIED` (`0x80070005`) and `TYPE_E_ELEMENTNOTFOUND` (`0x8002802B`). These earlier results could not be reproduced against the user's currently open session because the executor cannot see its process or ROT entry.

## Feature extraction counts

| Measure | Result |
|---|---:|
| Parts opened successfully in SOLIDWORKS during this attempt | 0 |
| Parts with FeatureManager read | 0 |
| Feature Cut features read | 0 |
| Cut features with complete parameters | 0 |
| Remaining Part/Cut inventory | 15 Parts remain `UNVERIFIED`; actual number of Cut features is unknown |

No FeatureManager results were simulated. Previous inventory and findings remain unchanged.

## Minimal next action

Run the reader from a PowerShell/C# process launched in the **same interactive Windows login/session and at the same elevation level** as SOLIDWORKS. First confirm that process can see `SLDWORKS.exe` and attach to `SldWorks.Application` through the ROT; only then open the sample Part Read-Only. If SOLIDWORKS was started with **Run as administrator**, the reader must use that same elevation; otherwise, launch both normally. Do not use a separate service or isolated Codex command session for the COM call.

After the reader runs in the accessible session, resume with the sample Part before processing the remaining Parts. Do not infer file corruption from `OpenDoc6` returning null with zero error/warning codes.

## Evidence labels

- `API_VERIFIED`: none in this attempt.
- `GEOMETRY_VERIFIED`: no new geometry verification in this attempt.
- `INFERRED`: the visible application and the Codex executor likely run in different COM/session contexts; this is the best explanation consistent with the current evidence, but is not yet confirmed.
