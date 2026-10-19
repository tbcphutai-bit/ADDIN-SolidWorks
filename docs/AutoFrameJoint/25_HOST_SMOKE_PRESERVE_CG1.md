# 25 — Auto Frame Joint host test: preserve Check Hole CG1 Release

Status: CODE PREPARED, NOT AUTHORIZED TO DEPLOY YET. This runbook supersedes the older Debug-baseline instructions in Runbook 24 for the current machine state.

## Verified from actual Anti GitHub report
Reviewed commit: 2bbc283579ff03ae1ce0447bcf640e26a20deb97.
Anti reports registered COM CodeBase C:\Tao moi\ADDIN.DLL.
Installed CG1 Release hash: D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E.
Original older Debug hash: F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126.
Isolated Auto Frame Joint test DLL hash: 643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B.
Anti stopped at Inspect; no deployment occurred.

## Decision
Option B: keep the current Check Hole CG1 Release baseline, NOT old Debug. Back up the CURRENT installed CG1 DLL and PDB; temporarily deploy the launcher test DLL/PDB; restore the exact original CG1 Release DLL/PDB afterward, whether test passes or fails.

## Exact code changes
File tools/AutoFrameJointHostSmoke/Invoke-AutoFrameJointHostTest.ps1:
- ExpectedInstalledSha256 pinned to D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E.
- Deploy requires BOTH -Approved and -ConfirmTemporaryCG1Replacement.
- It explicitly warns that it will temporarily replace CG1.
- Existing guards retained: COM CodeBase validation, test DLL SHA, paired DLL/PDB backups, manifest, rollback hashes.
No C# source, ADDIN.csproj, SWAddin, COM registry, Check Hole, Mirror or Bend changes.

## STEP A — READ ONLY (may execute now)
Stop all concurrent builds that might write C:\Tao moi\ADDIN.dll.
Fetch this branch in a clean isolated worktree; read the script and verify it has not been modified.
Run PowerShell:
powershell.exe -NoProfile -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Inspect

Require:
1. Registered CodeBase matches C:\Tao moi\ADDIN.dll.
2. Installed hash matches D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E.
3. Test hash matches 643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B.
4. SOLIDWORKS is closed. Original and test PDB files exist.
If ANY fail, STOP and report; do not change hashes blindly.

## STEP B — DEPLOY (DO NOT RUN BEFORE USER RECONFIRMS CG1 TEMPORARY REPLACEMENT)
After renewed approval, Inspect PASS, and SOLIDWORKS closed:
powershell.exe -NoProfile -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Deploy -Approved -ConfirmTemporaryCG1Replacement

Record exact Manifest path locally, not in public GitHub. The script backs up the CURRENT CG1 DLL/PDB, validates backup SHA, then installs the test pair. If any error occurs, stop.

## STEP C — HOST SMOKE
Start SOLIDWORKS, test only a COPY of a simple Assembly.
1. Giao góc Frame tile enabled only with Assembly.
2. Frame A and B native PMP selection.
3. Reject duplicate component; allow two valid Part occurrences.
4. Analyze read-only status persists, changing B invalidates stale result.
5. Cancel/Esc closes safely, no changes to Part/Feature/Mate/documents.
6. No Preview3D/ApplyCut yet.
Stop if the Add-in fails to load or a model is unexpectedly modified.

## STEP D — RESTORE ALWAYS
Close SOLIDWORKS. Use the real Manifest path from Deploy:
powershell.exe -NoProfile -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Restore -Approved -ManifestPath "<ACTUAL_DEPLOYMENT_JSON_PATH>"

Verify installed DLL SHA equals D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E, and installed PDB hash matches the backup manifest.
If Restore fails, STOP. Do not bypass checks.

## Anti output
Publish a new review-only report with Inspect output, actual host PASS/FAIL per scenario, exception screenshots where permissible, deployment source SHA, and restored CG1 hash. Keep customer Assembly files and private local manifest paths off public GitHub. No C# changes or merging. ChatGPT will review the actual report and code before any next change.

## Build collision prevention
The shared project configuration copies outputs into C:\Tao moi\ADDIN.dll. Future builds must set an isolated OutputPath AND disable PostBuildEvent. Do not run another Check Hole build while a temporary host test is active.