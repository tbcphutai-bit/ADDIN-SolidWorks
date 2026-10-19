# 27 — Restore collision after AFJ deploy: STOP, read-only check first

**ChatGPT code-owner review:** Actual Anti report committed at `249593de8d9f54f50449d348a11d5344a4ee1617`. This document and the companion PowerShell script do not modify Add-in C# or installed DLLs.

## Proven from report and original script
- Antigravity completed original Plan A Debug F8 preflight and `Deploy -Approved`. Manifest created: `host-test-20261019-214104-728\deployment.json`.
- Actual SOLIDWORKS native PMP **NOT TESTED**: user couldn't open SOLIDWORKS then requested restore.
- Background CG2 build overwrote the shared installed `C:\Tao moi\ADDIN.dll` after AFJ deployment. Installed **DLL** was reported as SHA256 `0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC` at 21:49:50.
- **Installed PDB SHA256 after that overwrite is NOT PROVIDED in the report**. Cannot claim the installed DLL and PDB match the archived CG2 pair.
- Original `Invoke-AutoFrameJointHostTest.ps1` `Restore` checks both installed DLL and PDB against original/test identities and correctly STOPPED on this unexpected new state. DO NOT disable or relax these checks, do not rerun restore hoping it works.
- Anti's report commits documentation only. No source or project changes were found in the corresponding GitHub commit.

## PHASE R0 — Freeze / read-only collision inventory (NOW)
1. Stop/suspend BOTH CG2 live runner/background builds and AFJ build processes that write to shared `C:\Tao moi\ADDIN.dll`; coordinate with owner of other workspace, without destructive termination. Save CAD work and fully close SOLIDWORKS before any later changes.
2. Checkout this review branch to a clean isolated worktree. Read **all** of `tools/AutoFrameJointHostSmoke/Inspect-HostCollision.ps1`.
3. Run ONLY this inspection:
```powershell
powershell.exe -NoProfile -File ".\tools\AutoFrameJointHostSmoke\Inspect-HostCollision.ps1"
```
4. Verify/record both installed DLL **AND PDB** full hashes, archived F8 pair, archived CG1 pair, archived CG2 Debug pair, AFJ test pair and manifest backup identities. Script does not copy, alter, delete, build, register COM or restore.
5. Return the real console output to ChatGPT. Put a sanitized report (no customer parts/manifests/secrets/full private unrelated paths) on a **separate review-only branch**; do not merge.

## DECISION TREE after inspection
- **Installed pair exactly archived CG2 Debug pair:** The existing CG2 Debug archive is confirmed. Still do not run old Restore blindly: old Restore will reject a CG2-installed DLL. Decide explicitly whether to leave CG2 installed or return to F8 per user's previously chosen Plan A.
- **Installed DLL matches CG2 but PDB differs from archived CG2:** This may be a mixed DLL/PDB pair from concurrent writes. Do NOT assert it's recoverable from archived CG2. Preserve the current installed pair in a NEW independent versioned archive, verify both SHA256 hashes, and request code-owner recovery procedure.
- **Installed hashes neither F8 nor CG2:** STOP. Another build may have landed. Repeat source identification, preserve pair, then decide.
- **Manifest backup does not match recorded original F8 hashes:** STOP. DO NOT restore from compromised/ambiguous backup. Seek known-good F8 archive and verification.

## PHASE R1 — Deliberate recovery, ONLY after ChatGPT reviews R0 result
- If choosing final Debug F8 (Plan A), we will author a **separate guarded recovery** from the verified F8 pair. It must FIRST preserve the actual currently-installed DLL/PDB pair, then restore a VERIFIED exact F8 DLL/PDB pair and re-hash both.
- Never rewrite the manifest to falsely label CG2 as the expected AFJ test; never bypass existing checks and never force-overwrite unverified builds.
- If user chooses to retain CG2, do not restore F8. Check whether installed CG2 PDB actually matches the CG2 build.
- Do not attempt another AFJ host test until shared build destination collision is eliminated.

## Structural fix before retrying host test
CG1, CG2, and AFJ builds must use distinct output folders **and** disabled/shared PostBuild copy tasks. OutputPath override alone or PostBuildEvent override alone can be insufficient. This is an integration/build-workflow fix to be reviewed separately; no global `ADDIN.csproj` modification in R0.

## Completion criteria
1. Installed DLL/PDB pair and archives fully inventoried with actual hash.
2. A verified independent snapshot of any unexpected installed build exists BEFORE eventual overwrite.
3. Restoration decision explicit, approved and hash-verified.
4. SOLIDWORKS native host status honestly marked NOT TESTED until tested.
5. No Check Hole/Mirror/Bend/CG2 sources touched by this audit.

**Scope now: READ-ONLY INSPECTION only.** No Deploy, Restore, copies, build, or registry modification until the collision evidence is reviewed.
