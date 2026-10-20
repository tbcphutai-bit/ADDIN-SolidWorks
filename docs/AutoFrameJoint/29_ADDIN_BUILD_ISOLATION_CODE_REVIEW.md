# 29 — Shared Add-in build isolation: code-owner patch (REVIEW ONLY)

**Baseline:** Antigravity recovery report `206b8ee2e6ecfea8089cbb49ba25eb57b140bbc1`.  
**Target:** `fix/shared-addin-build-output-isolation-20261020`.  
**Scope:** `ADDIN.csproj` only plus this documentation. **No C# changes. Not merged. Not deployed. Not built here.**

## Ground truth (actual ADDIN.csproj)
Previously:
- `Debug|x64` has `<OutputPath>C:\Tao moi\</OutputPath>`. Its output itself is placed at the COM-registered install directory.
- An unconditional `PostBuildEvent` copies `$(TargetDir)$(TargetName).dll` and `.pdb` to `C:\Tao moi\ADDIN.dll` and `ADDIN.pdb`. It affects Release builds as well.
- Multiple agents and worktrees therefore overwrite the same installed pair without user approval.

## Exact patch authored (not pseudocode)
Inside `Debug|x64` property group:
```xml
<!-- Keep Debug builds inside the current worktree, never in the registered Add-in folder. -->
<OutputPath>bin\x64\Debug\</OutputPath>
```
Inside final PropertyGroup after `Microsoft.CSharp.targets` import:
```xml
<!-- No automatic deploy. Deploy only via a separate reviewed backup/hash/restore operation. -->
<PostBuildEvent></PostBuildEvent>
```

This makes normal project builds put Debug output into `<worktree>\bin\x64\Debug\`; Release remains `<worktree>\bin\x64\Release\`. A normal rebuild must not copy DLL/PDB to the installed COM path.

**Critical limit:** A different branch/worktree still using an OLD `ADDIN.csproj` can overwrite `C:\Tao moi\ADDIN.dll`. This patch protects ONLY builds performed from worktrees that contain it. All CG2/Codex/Antigravity builds must be coordinated; DO NOT merge to CG2 or another branch automatically.

## Instructions to Antigravity — EXACT review workflow

1. Suspend all **background builds** into `C:\Tao moi`. Save and preserve other worktrees. Read this file and `ADDIN.csproj` in patch branch. Do not modify Check Hole, Mirror, Bend, SwAddin, feature source or registry.
2. Use a separate clean clone/worktree of this branch. Do NOT checkout over dirty user changes; do not alter active COM registration.
3. Before build, in Windows PowerShell:
```powershell
$installDir = 'C:\Tao moi'
$installedDll = Join-Path $installDir 'ADDIN.dll'
$installedPdb = Join-Path $installDir 'ADDIN.pdb'
$baselineDll = (Get-FileHash $installedDll -Algorithm SHA256).Hash
$baselinePdb = (Get-FileHash $installedPdb -Algorithm SHA256).Hash
Write-Host "BASELINE DLL=$baselineDll PDB=$baselinePdb"
```
Expected (per Anti recovery report only): `F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126` DLL, `754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097` PDB. If hashes differ, STOP and ask ChatGPT; another build may have started.
4. Confirm no installed SOLIDWORKS is running (and that dependent work is saved). Build **this same project**, not a solution from another branch:
```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild ".\ADDIN.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /p:KillSolidWorksOnBuild=false /v:minimal
$buildExit = $LASTEXITCODE
Write-Host "BUILD_EXIT=$buildExit"
```
The MSBuild path must exist on the test machine, otherwise locate the installed MSBuild with `vswhere` rather than inventing a version. The test must NOT require disabling guards or editing source files.

5. Check actual build output `bin\x64\Debug\ADDIN.dll` and `ADDIN.pdb`. Verify the SAME installed pair hashes remain unchanged:
```powershell
Write-Host "AFTER DLL=$((Get-FileHash $installedDll -Algorithm SHA256).Hash)"
Write-Host "AFTER PDB=$((Get-FileHash $installedPdb -Algorithm SHA256).Hash)"
Get-Item ".\bin\x64\Debug\ADDIN.dll", ".\bin\x64\Debug\ADDIN.pdb" |
  Select-Object FullName,Length,LastWriteTime
git status --short
git diff --check
```
Compare AFTER to BASELINE captured in the SAME session. If any changed: **STOP immediately**, keep log, do not try restoring blindly. No host test yet.
6. Run equivalent Release x64 build only if appropriate, with the same unchanged-installed hashes guard. This is valuable because the original PostBuildEvent was unconditional.
7. Report a new review-only commit containing console output (sanitize private paths, no DLL/PDB binaries) and version metadata:
   - HEAD commit and changed-file list;
   - Debug x64 Build exit, output path, original/after installed SHA256 DLL/PDB;
   - Release x64 result if run;
   - detailed build warnings/errors;
   - no changes to feature source;
   - note whether CG2 branch still uses old project (if so, collision risk remains).

## Acceptance rules
- Isolated build output under the worktree, not the registered COM folder.
- `C:\Tao moi\ADDIN.dll` and `ADDIN.pdb` both unchanged before/after.
- No `PostBuildEvent` copy to installed DLL/PDB.
- No feature code changed and no COM registration/deployment.
- **Only after all builds/worktrees adopt a safe isolation policy can Host Smoke testing resume.**

## Important caveat
Switching `OutputPath` may affect existing local scripts that assumed output at `C:\Tao moi`; these must be audited and explicitly updated on their own reviewed branches. Do not silently modify/check-in Check Hole project workflows without their code owner's review.
