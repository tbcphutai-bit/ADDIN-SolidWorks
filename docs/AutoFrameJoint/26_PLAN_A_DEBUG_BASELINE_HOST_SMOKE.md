# 26 — AUTO FRAME JOINT: PLAN A — old Debug baseline, exact restore

**User updated their decision**: use **Plan A**, not Plan B. This is an operational plan, not proof that files have already changed on Windows.

**Parent source:** \`review/antigravity-host-smoke-inspect-stop-20261019\`, SHA \`2bbc283579ff03ae1ce0447bcf640e26a20deb97\`.  
**Branch:** \`docs/afj-host-plan-a-debug-baseline-20261019\`.

## Critical distinction: plan A changes the FINAL installed Add-in
- **Baseline A** = old Debug DLL SHA256 \`F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126\`.
- **Current CG1 Release in Anti's earlier report** = SHA256 \`D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E\`.
- **Auto Frame Joint test DLL** = SHA256 \`643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B\`.
- Plan A workflow: **Debug F8 is installed BEFORE Deploy -> AFJ test DLL installed temporarily -> old Debug F8 restored AFTER test**. It will NOT restore CG1 Release. This can mean older Check Hole capabilities are active afterward. To preserve CG1, first confirm that the CG1 Release DLL/PDB pair exists in an independent verified backup. Do not lose that build.
- We do not know the LIVE local hash after the user's last message. Verify; do not infer that the user already restored Debug.

## Code/script selection
Use original script from this same branch:
\`tools/AutoFrameJointHostSmoke/Invoke-AutoFrameJointHostTest.ps1\` at blob SHA \`cae3fbc0e74de9cf14d7b857c37a9fd9861d9736\`.
This version intentionally expects installed F8 Debug; **DO NOT use the B script at branch \`fix/afj-host-deploy-preserve-cg1-20261019\`** because it pins CG1 hash and has different approval flags.

## Gate 0 — No concurrent builds and no unverified overwrite
1. Stop Antigravity/Copilot/VS rebuilds that may touch \`C:\Tao moi\ADDIN.dll\`, save work, and exit SOLIDWORKS fully.
2. Check actual registered COM CodeBase (using original script's read-only Inspect); do not use regasm or change registry.
3. Before changing *any* file, verify exact DLL and PDB at:
   - Installed pair: \`C:\Tao moi\ADDIN.dll\`, \`C:\Tao moi\ADDIN.pdb\`.
   - Debug archived pair (Anti's reported location): \`C:\Tao moi\backup_addin_pre_launcher_smoke\ADDIN.dll\`, \`C:\Tao moi\backup_addin_pre_launcher_smoke\ADDIN.pdb\`.
   - Test pair: \`C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\bin\x64\Debug\ADDIN.dll\` / \`ADDIN.pdb\`.
4. Check debug archive SHA F8 and actual installed SHA. Check original archived PDB SHA equals installed PDB SHA if Debug is already installed. The old report verified the backup **DLL**, not necessarily matching PDB: **do not claim it is a valid pair until inspected**.
5. Verify a separate CG1 Release backup DLL/PDB with the pinned Release hash D925; if none is available, report that irreversible regression risk explicitly and STOP BEFORE overwriting CG1.
6. If installed SHA = **F8** and archived PDB matches installed, SKIP all restoration/copying and continue to Gate 1.
7. If installed SHA = **D925**, do **NOT** silently change it: snapshot both CURRENT CG1 DLL/PDB to a timestamped backup outside the installed folder, verify SHA/PDB hashes, and only then consider user-authorized Plan A restoration from F8 backup.
8. If installed SHA is neither F8 nor D925, STOP and ask ChatGPT to review the actual new build. Never blindly update the expected SHA constant.

### READ-ONLY PowerShell preflight (no writes)
Run from any Windows PowerShell:
\`\`\`powershell
$installed = 'C:\Tao moi\ADDIN.dll'
$installedPdb = 'C:\Tao moi\ADDIN.pdb'
$oldDebug = 'C:\Tao moi\backup_addin_pre_launcher_smoke\ADDIN.dll'
$oldDebugPdb = 'C:\Tao moi\backup_addin_pre_launcher_smoke\ADDIN.pdb'
$testDll = 'C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\bin\x64\Debug\ADDIN.dll'
$testPdb = 'C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\bin\x64\Debug\ADDIN.pdb'

$files = @($installed, $installedPdb, $oldDebug, $oldDebugPdb, $testDll, $testPdb)
$files | ForEach-Object {
  if (Test-Path -LiteralPath $_ -PathType Leaf) {
    $f = Get-Item -LiteralPath $_
    $h = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    [pscustomobject]@{ Name=$f.FullName; Bytes=$f.Length; SHA256=$h.Hash }
  } else { Write-Warning "MISSING: $_" }
}
Get-Process SLDWORKS -ErrorAction SilentlyContinue | Select-Object Id,ProcessName
\`\`\`
**The output above is evidence, not permission to overwrite missing/mismatched files.**

## Gate 1 — Inspect using the ORIGINAL Debug-pinned script
From the exact clean branch/worktree:
\`\`\`powershell
powershell.exe -NoProfile -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Inspect
\`\`\`
Require:
- Installed SHA exactly F8.
- Test SHA exactly 643A.
- CodeBase exactly \`C:\Tao moi\ADDIN.dll\`.
- All four deployed/backup DLL/PDB pair files valid, SOLIDWORKS closed.
- No concurrent build to shared installed directory.
If Debug is not installed yet, do not run Deploy. Restoring Debug is a separate deliberate file operation requiring the verified CG1 backup above.

## Gate 2 — Deploy (ONLY after Gate 1 PASS)
User has approved controlled AFJ host smoke testing, and has now requested Plan A. Anti may proceed **only** if installed is already the verified F8 Debug baseline or after backup-safe, explicitly reviewed Debug restoration; do not infer local disk state from user wording.

\`\`\`powershell
powershell.exe -NoProfile -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Deploy -Approved
\`\`\`
The script independently backs up the installed Debug DLL/PDB, validates hashes and creates \`deployment.json\`. Require \`DEPLOYED TEST DLL ONLY\` and an exact \`Manifest:\` path.
If any error, STOP and do not start SOLIDWORKS; preserve backup.

## Gate 3 — SOLIDWORKS smoke test using COPIED Assembly only
1. Open SOLIDWORKS; confirm \`Giao góc Frame\` tile appears and is enabled only in Assembly.
2. Open PropertyManager: separate Frame A and Frame B selection boxes.
3. A only -> Analyze disabled; distinct loaded Part A/B -> Analyze enabled; duplicate rejected.
4. Click Analyze -> provisional result persists; change B -> prior result clears.
5. Switch ActiveDoc -> fails closed; Cancel/Esc -> no lingering page, no COM exception.
6. Open twice; check no duplicate PropertyManager.
7. FeatureManager/Mates/Config/document dirty state must NOT change. No Preview3D, no ApplyCut, no saving test Assembly.

Record actual screenshots and error/log output. Do not claim PASS based on earlier standalone build.

## Gate 4 — Mandatory Restore to DEBUG F8 (Plan A final state)
Fully close SOLIDWORKS. Using **the exact Manifest path printed by Gate 2**:
\`\`\`powershell
powershell.exe -NoProfile -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Restore -Approved -ManifestPath "<ACTUAL_MANIFEST_FILE_PATH>"
\`\`\`
Check both restored hashes against manifest and installed DLL hash F8. The CG1 Release snapshot must remain in its own backup unchanged and **is not automatically reinstalled**. If Restore fails, STOP, retain manifest/backups, and report verbatim failure.

## Anti handoff
- No source edits, no COM registry edits, no merge to main, no Check Hole code changes.
- New report-only branch, file \`docs/AutoFrameJoint/ANTIGRAVITY_HOST_SMOKE_PLAN_A_REPORT.md\`.
- Exact Inspect hashes, debug archive DLL/PDB hashes, CG1 independent backup existence/hash, Deploy manifest status (redact full private path on public repo), test PASS/FAIL per case, Restore hash and proof.
- If any precondition fails: report STOP, do not work around SHA or PDB checks.
- ChatGPT will directly review actual GitHub report and source before next step.

## Important
Restoring F8 after test is **by design in Plan A**; it is not the newer CG1 build. Make sure the user is aware that Check Hole CG1 Release will not remain installed afterward. 
