# 28 — Phase R1: guarded recovery from installed CG2 Debug to original F8

**Code owner:** ChatGPT  
**Source of truth:** actual Anti Phase R0 review `7dec8a45c2a5b6a6935d9ecbf31a862b15a646ca`.  
**Status:** PowerShell recovery code AUTHORED but NOT run on Windows and NOT proven at runtime.  
**Branch:** `fix/afj-guarded-recover-f8-from-cg2-20261020`.

## What I directly verified in GitHub report

- Currently installed CG2 Debug DLL/PDB is a matching pair, verified by Anti with PowerShell hashes.
  - DLL `0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC`
  - PDB `C73F9B713AB3279E068AA8F98D7E3C979BBB72EE992A57E6CF1C51054E84A128`
- Independent archived CG2 Debug pair matches both installed bytes/hashes.
- Original Debug F8 pair stored both in manifest backups and archive:
  - DLL `F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126`
  - PDB `754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097`
- Original AFJ manifest is intact and points to the installed `C:\Tao moi\ADDIN.dll`.
- A `devenv` process WAS still running at time of R0 inspection, despite no MSBuild or csc. Close Visual Studio normally before any recovery, and coordinate with the Check Hole CG2 agent to pause builds.
- SOLIDWORKS Host Smoke was **NOT RUN**. AFJ deployment itself took place but was superseded by the newer CG2 build. Original `Restore` correctly stopped.

## Decision

Follow user's previously selected **Plan A** for the recovery final state: **Debug F8 installed**, NOT CG2 Debug. CG2 source and exact archived CG2 DLL/PDB must be preserved first.

**This is a separate intentional recovery operation, not the legacy AFJ Restore.** Do NOT rerun `Invoke-AutoFrameJointHostTest.ps1 -Mode Restore`, and do not edit the old manifest or hash constants to force it to pass.

## Code owner wrote a guarded script

`tools/AutoFrameJointHostSmoke/Recover-PlanAF8FromCG2.ps1`

Modes:
- `Inspect` (default): read-only check of COM registration and archived CG2/F8/manifest hashes, report current install hashes and active build/CAD processes.
- `Recover`: requires **BOTH** `-Approved` and `-ConfirmReplaceInstalledCG2`; checks no SOLIDWORKS/Visual Studio/MSBuild/dotnet/csc/VBCSCompiler processes, verifies installed CG2 pair, makes a NEW independent CG2 snapshot and validates both hashes, stages F8 DLL/PDB, rechecks CG2, attempts file replacement and verifies installed F8. On failure, attempts a guarded CG2 rollback only if installed hashes remain known; preserves all backups and journals result.

**Note:** DLL and PDB replacements are separately atomic, not jointly transactional. A power outage, external writer, antivirus lock or script failure can still leave a partial state. The script attempts guarded rollback but must NOT be called infallible. Backup and log must always be preserved.

## Step 0 — stop all competing writers

Notify the Check Hole CG2 and AFJ agents to stop ALL shared-directory builds and all auto-build/watch actions. Wait for them to acknowledge. Stop Visual Studio safely (save source edits first), save CAD work, and close SOLIDWORKS. Do **not** force-kill user processes. Stop if processes return unexpectedly. Do not reset, stash, clean or merge worktrees.

## Step 1 — inspect only

Fetch this exact branch in an isolated clean worktree. Read the entire script. Then run:

```powershell
powershell.exe -NoProfile -File ".\tools\AutoFrameJointHostSmoke\Recover-PlanAF8FromCG2.ps1" -Mode Inspect
```

Record raw output; all expected CG2/F8/archive/manifest hashes must match EXACTLY. If anything differs, stop and send to ChatGPT. If `ACTIVE devenv`, `ACTIVE MSBuild`, etc. appears, close safely and rerun Inspect. No edits to hash constants.

## Step 2 — guarded recovery (Plan A already selected)

After exact Inspect PASS and explicit confirmation that shared build writers are paused, run:

```powershell
powershell.exe -NoProfile -File ".\tools\AutoFrameJointHostSmoke\Recover-PlanAF8FromCG2.ps1" -Mode Recover -Approved -ConfirmReplaceInstalledCG2
```

The destination is the same registered `C:\Tao moi\ADDIN.dll` and `ADDIN.pdb`. This will intentionally replace the CG2 Debug install with older Debug F8. It does not delete the archived CG2 pair or edit code/registry.

If the script fails, **do not** run old Restore or repeat blindly. Keep the recovery folder and entire console output. Do not start SOLIDWORKS until installed pair is verified.

## Step 3 — post-recovery validation

```powershell
Get-FileHash "C:\Tao moi\ADDIN.dll" -Algorithm SHA256
Get-FileHash "C:\Tao moi\ADDIN.pdb" -Algorithm SHA256
```

Required exact final pair:
- DLL: `F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126`
- PDB: `754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097`

Keep `recovery.json` locally; it documents the operation and independent CG2 backup.

**Important:** Plan A returns to the older F8 Add-in. New CG2 Check Hole functions present in the more recent CG2 build may not be available until that build is deployed again intentionally.

## Step 4 — submit evidence to ChatGPT

Antigravity prepares `docs/AutoFrameJoint/ANTIGRAVITY_F8_RECOVERY_REPORT.md` on a separate review-only branch:
- source branch, commit, script SHA;
- Inspect output and exact current installed/CG2 archive/F8 archive/manifest hashes;
- confirmation VS and build agents were paused;
- Recover command, exit code, all errors and journal status;
- final installed DLL and PDB SHA256;
- CG2 independent backup verification and retained archive;
- any skipped steps identified as NOT TESTED;
- all C# source file diffs (expected NONE).

Do not include customer CAD geometry, personal paths outside needed technical context, or upload DLL/PDB binaries.

ChatGPT must read the exact report and code before proceeding. Do not merge anything into main. No additional AFJ Deploy or Host Smoke Test until an isolated build/deployment workflow is approved.

## Future root-cause fix

The CG2 runner and AFJ launch test collided because they use the same installed `C:\Tao moi\ADDIN.dll` path. Later, code owner should change each branch's build job to use its own isolated OutputPath and disable the PostBuild copy; a single explicitly gated deployment step should own changes to the installed path. Not part of this recovery phase.
