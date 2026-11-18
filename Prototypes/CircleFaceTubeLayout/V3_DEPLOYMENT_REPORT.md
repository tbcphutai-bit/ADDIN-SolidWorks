# Circle Along Face V3 – Deployment & Verification Report

## 1. Root Cause: Why SOLIDWORKS Was Loading V1 Instead of V3

1. COM Add-in registration in Windows Registry:
   - Key: `HKLM\SOFTWARE\SolidWorks\AddIns\{d1c7f4b2-9a1e-4b5c-b234-a1b2c3d4e5f6}` (`Title = TAI TOOL`)
   - `CLSID\{d1c7f4b2-9a1e-4b5c-b234-a1b2c3d4e5f6}\InprocServer32`: `CodeBase = file:///C:/Tao moi/ADDIN.DLL`
2. Visual Studio (`devenv.exe`) had `C:\Tao moi\ADDIN` open on branch `fix/check-same-part-report-first-v2-20261106` (`dc7cc87`), where untracked `Commands/CircleFaceTubeLayout/` still contained the **V1** implementation.
3. In `ADDIN.csproj`, `Debug|x64` sets `<OutputPath>C:\Tao moi\</OutputPath>` and `PostBuildEvent` copies `$(TargetDir)$(TargetName).dll` to `C:\Tao moi\ADDIN.dll`. Building in Visual Studio therefore overwrote `C:\Tao moi\ADDIN.dll` with V1 at `2026-11-18 08:02:52`:
   - Old Size: `9,080,320` bytes
   - Old SHA256: `94C467B2AFA0FA891D4965F442354E028F8D911D19B2AC698D9C15952FAA63A6`
   - Old MVID: `141bfd52-9978-4f5c-a888-e2970fcaf15f`

> **Important Note for Self-Rebuild (`ADDIN` vs `ADDIN-CircleFaceV3`):**
> `C:\Tao moi\ADDIN` was left untouched to preserve uncommitted work on `fix/check-same-part-report-first-v2-20261106`. Rebuilding `C:\Tao moi\ADDIN` directly in Visual Studio will overwrite `C:\Tao moi\ADDIN.dll` with V1 unless `Commands/CircleFaceTubeLayout/` is synced or you build from the V3/quickfix worktree.

---

## 2. Backup & Isolated V3 Build (`d18132b`)

All builds were run from `C:\Tao moi\ADDIN-CircleFaceV3` (`feature/circle-face-v3-smart-overhang-20261117` @ `d18132b`) with isolated output paths, `/p:PostBuildEvent=`, and `/p:RegisterForComInterop=false` without modifying any other command or `C:\Tao moi\ADDIN`:

1. **`Debug|x64` (`bin\CircleFaceV3\Debug\ADDIN.dll`)**: **PASS** (exit code `0`)
   - Size: `7,234,048` bytes (`ADDIN.pdb`: `10,407,424` bytes)
   - SHA256: `604D06D5B7981B3392BD0B5FD439CEC0C9EFFB73F48FFCD976AD239BD7F9E6C2`
   - MVID: `89c0a053-4eaf-4d6d-a07c-6207b763438a`
   - Verified UTF-16LE user strings present: `"Circle Along Face V3 - Smart Overhang"`, `"Equal pitch at both ends"`, `"Allow symmetric overhang"`, `"START solver=3"`
2. **`Release|x64` (`bin\CircleFaceV3\Release\ADDIN.dll`)**: **PASS** (exit code `0`)
   - Size: `6,638,592` bytes (`ADDIN.pdb`: `9,240,064` bytes)
   - SHA256: `A025715C3DB49E4084A2C6A307D2D07D89D6BD34F900091D8CB62B93DDEE7498`
   - MVID: `c27a2f4b-2f63-4d01-b37b-990a5ca23da2`
3. **`GeometryTests.exe` (`/platform:x64 /warnaserror`)**: **PASS** (exit code `0`)
   - `PASS finite caps, crossing, 3D separation bounds, tilted overlap, arc/chord regression`
   - `PASS topology splits, reverse orientation, non-four-sided, ambiguous and degenerate boundaries`
   - `PASS custom equal-pitch small/large ends, balanced margins, twist/flip, and 3D collision checks`
   - `PASS V3 opt-in negative cap margins, N30 baseline, N40/H2, N41/H12 + curved/twisted/flip + minGap=0.20mm geometry`
   - `PASS all geometry tests`
4. **Backup & Deploy** (while `SLDWORKS.exe` was closed):
   - Backed up `C:\Tao moi\ADDIN.dll` -> `C:\Tao moi\ADDIN.backup_v1_20261118_081955.dll`
   - Backed up `C:\Tao moi\ADDIN.pdb` -> `C:\Tao moi\ADDIN.backup_v1_20261118_081955.pdb`
   - Copied `bin\CircleFaceV3\Debug\ADDIN.dll` & `.pdb` to `C:\Tao moi\ADDIN.dll` & `.pdb`
   - Post-copy SHA256 verified identical (`604D06D5B7981B3392BD0B5FD439CEC0C9EFFB73F48FFCD976AD239BD7F9E6C2`), MVID `89c0a053-4eaf-4d6d-a07c-6207b763438a`.

---

## 3. Live SOLIDWORKS 2024 Startup Verification & Side-Effect Note

1. **Startup & COM Load Proof (`SLDWORKS.exe`, revision `32.5.0` / 2024 SP5.0)**:
   - DBWIN `[TAI STARTUP]` trace captured directly from `SLDWORKS.exe`:
     ```
     [TAI STARTUP] Begin 2026-11-18T08:23:40.0400925+07:00; Assembly=C:\Tao moi\ADDIN.dll; MVID=89c0a053-4eaf-4d6d-a07c-6207b763438a
     [TAI STARTUP] Callback registration ms=1
     [TAI STARTUP] CreateTaskpaneView2 ms=5
     [TAI STARTUP] AddControl + UI constructor ms=399
     [TAI STARTUP] UI Init ms=27
     [TAI STARTUP] ConnectToSW total ms=434
     ```
   - `swApp.GetAddInObject("{D1C7F4B2-9A1E-4B5C-B234-A1B2C3D4E5F6}")` returned non-null (`ADDIN_AUTO_LOADED=True`, `ADDIN_OBJECT_NONNULL=True`).
   - `live_verify_6866.SLDPRT` opened with `warnings=0`, persisted face selected (`err=0`, `Select4=True`).
2. **UI Automation & Active Session Stop**:
   - UIA automation was stopped before completing the in-dialog `N=30/40/41`, `Cancel`, `Create 3D Sketch`, and `Undo` clicks (so `TUBE_FACE_*.log` and the V3 dialog screenshot on `copy_6866` remain to be captured after the quickfix build is deployed).
   - **Cross-command side-effect note**: `feature/circle-face-v3-smart-overhang-20261117` (`d18132b`) was branched before commit `b1457ec`, so deploying it to `C:\Tao moi\ADDIN.dll` surfaced the pre-`b1457ec` Drawing View selection behavior in `Fix Tỉ lệ` and `DIM Kegaki`. Codex has already fixed those two Drawing handlers in `circle-v3-drawing-quickfix` without modifying `CircleFaceTubeLayout`; per user instruction, no other classes were touched here and `C:\Tao moi\ADDIN.dll` was left for the user's own rebuild.
