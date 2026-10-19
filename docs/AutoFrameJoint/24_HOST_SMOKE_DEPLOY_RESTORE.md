# 24 — SOLIDWORKS Host Smoke Test (Approved by user)

**User approved a controlled test deployment on 2026-10-19.**  
**Branch**: `docs/auto-frame-joint-host-deploy-restore-20261019`  
**Reviewed launcher SHA**: `1c6f27d015bdc5814d6dd2a27d5a45871eaf2ea6`  
**Anti build report SHA**: `fe99e650ed0ae6e334e5eedba6d7be5116e02648`.

## Explicit scope
- Nạp đúng `ADDIN.dll` Debug x64 from Anti's isolated build **temporarily** to exercise native PMP selection of A/B, Analyze, Cancel.
- **No source editing**, no additional COM registration, no regasm, no registry writing, no reboot requirement.
- **No Preview 3D and no Apply Cut**; buttons for these are not implemented.
- Test only a COPY of a nonconfidential Assembly; do not test a production assembly.
- **Always restore the original ADDIN.dll/PDB after testing**, even if tests pass.

## What I checked in the actual source
- `SWAddin.cs`: registered add-in class has GUID `D1C7F4B2-9A1E-4B5C-B234-A1B2C3D4E5F6`; it prints the actual `typeof(SwAddin).Assembly.Location` at startup to trace.
- `ADDIN.csproj`: Debug|x64 outputs directly to `C:\Tao moi\`. Build must override **OutputPath as well as PostBuildEvent**, even with the latter disabled.
- `UI/AutoFrameJointTaskPaneLauncher.cs`: calls `AutoFrameJointCommandEntry.Open(swApp)`; the button is only for Assembly.
- `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`: A mark = 1, B mark = 2; selections are separate.
- `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs`: reports provisional analysis only.

## 0 — STOP criteria; do not bypass
1. Stop all Visual Studio/Anti builds and close SOLIDWORKS **after saving open work**.
2. If user is running a different registered `ADDIN.dll` from `C:\Tao moi\ADDIN.dll`, **do not overwrite**; contact code owner. This is checked using managed COM `CodeBase`. If registration is unavailable/ambiguous, **stop**; don't run regasm.
3. If installed DLL hash differs from report `F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126`, stop. If test DLL hash differs from `643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B`, stop. Re-review current binaries rather than overriding checks.
4. If an original or test PDB is absent, stop; require a complete matched pair.
5. Do not attempt testing in production model or alongside running SOLIDWORKS.

## 1 — Get the scripts
File: `tools/AutoFrameJointHostSmoke/Invoke-AutoFrameJointHostTest.ps1` in this exact branch. It has three modes `Inspect`, `Deploy`, `Restore`. Default is **read-only Inspect**.

Read the script fully before running. It has NOT been executed on the user's Windows machine by ChatGPT. If PowerShell syntax/API errors occur, report them before modifying anything.

## 2 — Inspect FIRST (read-only)
Open Windows PowerShell in the isolated worktree:
```powershell
powershell.exe -NoProfile -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Inspect
```

It shows:
- whether `SLDWORKS.exe` is running;
- registered managed COM `CodeBase` paths for the source add-in GUID;
- original and test DLL SHA256;
- expected hashes.

**Required gate**: Confirm registered CodeBase path agrees with intended installed DLL and hashes agree. If there is **no CodeBase**, stop. Ask user for actual loaded assembly trace `[TAI STARTUP] Assembly=...`; do not guess or modify registry to make the script pass.

## 3 — Deploy once (user approved)
Once Inspect has passed, SOLIDWORKS is closed, and the paths/hashes have been checked:
```powershell
powershell.exe -NoProfile -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Deploy -Approved
```

The script creates a timestamped local backup containing original `ADDIN.dll`, `ADDIN.pdb` and `deployment.json` manifest. It copies the pinned launcher test DLL/PDB to the verified installed location and confirms SHA256. It does not change the COM registry.

**Record the exact `Manifest:` path printed by the script**. If Deploy reports any error, do not attempt to run SOLIDWORKS; send exact error to ChatGPT.

## 4 — Actual SOLIDWORKS test
Start SOLIDWORKS normally, open the **COPY** of a sample Assembly. Do NOT change Add-in registration. If SOLIDWORKS reports the add-in missing, startup exception, or model trouble, immediately stop and restore (step 5).

| # | Scenario | Expected read-only result |
| --- | --- | --- |
| 1 | Start without Assembly / with Part | `Giao góc Frame` disabled |
| 2 | Open a sample Assembly | `Giao góc Frame` visible and enabled under Model → Macro |
| 3 | Click launcher | Native PropertyManager with Frame A / Frame B selection boxes |
| 4 | Select one Part in A only | Analyze disabled |
| 5 | Select one DIFFERENT Part in B | Analyze enabled; selection marks 1/2 |
| 6 | Select same component for both | Rejected; never two copies of same occurrence |
| 7 | Click Analyze | Short status appears, persists; no cut or geometry changes |
| 8 | Change B selection | Old Analyze status clears |
| 9 | Switch active Assembly | Safe refusal; no cross-document analysis |
| 10 | Cancel / Esc, open again | Old PMP released, no duplicate/exception |
| 11 | Check FeatureManager, mates, configurations and document dirty flags | No additions/changes |

**Report any surprising behavior exactly.** If valid components are rejected with COM `swObjectUnsupported`, capture the component situation and stop; do not force the identity check open. Avoid testing Part vs Subassembly beyond safe selection if model is fragile.

Record screenshots of native PMP and tool Output/trace (remove confidential filenames before sharing); collect any exception text, taskpane state and SOLIDWORKS version. This is a host smoke test, NOT validated cutting.

## 5 — Mandatory rollback after the test (PASS or FAIL)
Close SOLIDWORKS fully. Restore using the exact manifest from Step 3:
```powershell
powershell.exe -NoProfile -File ".\tools\AutoFrameJointHostSmoke\Invoke-AutoFrameJointHostTest.ps1" -Mode Restore -Approved -ManifestPath "C:\Tao moi\ADDIN-AutoFrameJoint-HostBackup\host-test-YYYYMMDD-HHMMSS-fff\deployment.json"
```
Replace the illustrative date with the **actual printed manifest path**. The script refuses if the current DLL/PDB do not match expected original/test identities.

Verify original SHA256 again:
```powershell
Get-FileHash "C:\Tao moi\ADDIN.dll" -Algorithm SHA256
```
Expected: `F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126`.
Then start SOLIDWORKS (optional) to confirm the original TAI TOOL works. If rollback fails, do not keep working on CAD until investigated.

## 6 — Required Antigravity review handoff
Do not call the launcher complete unless Actual SOLIDWORKS test results are received.
Prepare `docs/AutoFrameJoint/ANTIGRAVITY_HOST_SMOKE_REPORT.md` on a **separate review branch** (redact local/customer/private paths, no sample Part content), containing:
- source launcher SHA and deployed DLL SHA;
- Inspect CodeBase and hashes (redact private unrelated paths);
- backup manifest location LOCAL ONLY (not on public GitHub);
- each test case PASS / FAIL / NOT TESTED;
- raw exception text and minimal anonymized screenshot links;
- confirmation no Part/mate/Feature edits;
- rollback operation and exact restored SHA;
- code files changed (should be ZERO);
- any observations about COM callbacks, A/B selection and status overwrite.

Ask ChatGPT for code changes only AFTER posting this report. ChatGPT must read the actual report and decide next patch. No merge into main, no Check Hole/Mirror/Bend code change, no Preview3D implementation in this stage.

## Important disclaimer
This script is authored from known paths/hashes reported in the repository, but cannot automatically know the user's live Windows COM registrations. It deliberately **fails closed** instead of guessing. If it stops, request a revised verified deployment plan rather than bypassing validation.
