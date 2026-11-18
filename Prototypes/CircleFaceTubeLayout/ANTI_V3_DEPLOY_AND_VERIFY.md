# HANDOFF FOR ANTI — DEPLOY THE CORRECT CIRCLE ALONG FACE V3 DLL

Date: 2026-11-18
Repository: https://github.com/tbcphutai-bit/ADDIN-SolidWorks
Working branch: `feature/circle-face-v3-smart-overhang-20261117`
Draft PR: https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/65

## The user-visible failure (confirmed)
The user launched SOLIDWORKS 2024 and opened the Circle Along Face dialog on the 6866 twisted surface Part. UI showed:
- "Large-end edge margin (mm)" and "Small-end 3D gap";
- Base count 30 + "Add +1" checked -> Total axes 31;
- **NO** `Small-end mode (Custom)`, `Equal pitch at both ends`, nor `Allow symmetric overhang (V3)`.

These strings match **V1** on `feature/circle-face-tube-layout-prototype-20261110`. In V2 and V3, the form shows "Both-end edge margin (mm)" and the added controls. The repository's current `main` has NO `Commands/CircleFaceTubeLayout` module at all. Both PR #64 and PR #65 are still draft/unmerged.

The user uploaded Visual Studio Output. It explicitly shows:
```
'SLDWORKS.exe' (CLR ...): Loaded 'C:\Tao moi\ADDIN.dll'. Symbols loaded.
[TAI STARTUP] ... Assembly=C:\Tao moi\ADDIN.dll; MVID=141bfd52-9978-4f5c-a888-e2970fcaf15f
```
Therefore, the running SOLIDWORKS DLL is **C:\Tao moi\ADDIN.dll**, which does NOT have the desired V3 form at this time. Prior Anti build reports used `bin\CircleFace\Debug\ADDIN.dll` / `bin\CircleFace\Release\ADDIN.dll` with `PostBuildEvent=` and `RegisterForComInterop=false`; a successful build there does NOT deploy to the registered DLL path.

## What to do — deployment, NOT a solver rewrite

1. Check out **latest** V3 branch above. Verify latest `CircleFaceTubeLayoutForm.cs` contains:
   - `Text = "Circle Along Face V3 - Smart Overhang"`
   - `Small-end mode (Custom)` and `Equal pitch at both ends`
   - `Allow symmetric overhang (V3)`, `Max cap overhang (mm)`.
   In `CircleFaceTubeLayoutCommand.cs`, verify `START solver=3` writes assembly_path and MVID.
2. Read project `ADDIN.csproj`. It has a post-build command that copies a DLL to **C:\Tao moi\ADDIN.dll** and masks its exit code by `set errorlevel=0`. **Do not assume Rebuild succeeded means the active DLL was replaced.**
3. Ask the operator to save all SolidWorks files and **close SOLIDWORKS normally**. Verify the process has fully exited. Do NOT taskkill/force-kill or overwrite a loaded DLL. Ensure no other session of SolidWorks is running.
4. Create a dated backup of **C:\Tao moi\ADDIN.dll** and **ADDIN.pdb** if it exists. Do not delete them.
5. Build the V3 branch in Visual Studio on Windows using SOLIDWORKS 2024 SP5.0 interop, .NET Framework 4.8, platform x64 (Debug and/or Release). **Disable the project's unconditional PostBuildEvent during the isolated build**, e.g. `/p:PostBuildEvent=` and `/p:RegisterForComInterop=false`, with a known isolated OutputPath. Record command and exit code; do not deploy an unverified build.
6. Confirm the DLL's build source commit is the latest V3 branch and that the built assembly includes the V3 controls and title; collect file SHA256 and assembly MVID.
7. Copy verified **ADDIN.dll** and optional matching **ADDIN.pdb** into **C:\Tao moi\** only while SOLIDWORKS is closed. Keep the COM registration path unchanged because Visual Studio Output already proves SOLIDWORKS loads this path. Compare post-copy SHA256 to built DLL and record in report.
8. Start SOLIDWORKS. In Visual Studio Output verify new `[TAI STARTUP]` MVID differs from old `141bfd52-9978-4f5c-a888-e2970fcaf15f`; confirm `Assembly=C:\Tao moi\ADDIN.dll`. Open Circle Along Face; title must say **Circle Along Face V3 - Smart Overhang** and all expected controls must be present. If it still shows V1, identify stale registration/duplicate DLLs, not just rebuild again.
9. Test 6866 copy, select exactly one face:
   - Uncheck `Auto count` (Custom)
   - Base count **30**, uncheck `Add +1` -> **Total axes 30**
   - Choose `Equal pitch at both ends`
   - D=20 mm, margin=5 mm, Flip=false, Overhang disabled -> Preview must show small/large independent pitches and small/large left/right cap margins. From Anti's previous 6866 bench: N30 pitch ~27.1675 / 48.2913 mm and cap L/R ~5 mm (depends on face selection).
   - Enable Smart Overhang max H=12: N30 must preserve original 5 mm margin (H=0); N40 6866 surface may fit with cap target H=0 (edge cap margin 0); N41 was reported to fit at H=9 mm on 6866 normal side. Recheck on current face and review every collision output. Do not hardcode these reference results.
   - Test `Add +1` checked on base count 30 -> total 31, not 30. Re-test switching modes and Flip.
   - Cancel -> no sketch; explicit Create -> only 3D construction axes; Undo -> restore. Use a **copy of the user's Part**.
10. Verify automatic debug log under `%LOCALAPPDATA%\TAI_TOOL\CircleFaceTubeLayout` includes `START solver=3`, `assembly_path`, `MVID`, selected mode, count, pitches, margins, GJK/BREP status, Overhang trials, and commit/undo.
11. User also supplied a generic Visual Studio Output with a first-chance `LayoutException` and many WPF binding warnings. These do not establish a crash; the process exited code 0. Investigate the actual CircleFace log if the command fails; do not confuse the existing independent TaskPane WPF binding warnings with missing V3 controls.
12. No unrelated command edits; do not merge PR #65 or #64. Existing V3 owner's-body/selected-face checks do not imply every assembly component is clear — document this separately.

## Deliverables
- A tested, correctly deployed DLL at the *existing registered path* **C:\Tao moi\ADDIN.dll**.
- Screenshot of the new V3 dialog showing Custom=30, Add+1 OFF, EqualPitch and SmartOverhang options, and the Preview result.
- Pre/post DLL SHA256, before/after MVID, commit hash, exact output and registered path, build command & exit codes.
- Real 6866 copy logs for N30/40/41; no geometry should be created by preview alone.
- Add `Prototypes/CircleFaceTubeLayout/V3_DEPLOYMENT_REPORT.md` to the same branch; update PR #65 comment. Do **not** merge.

**Important:** Never report "V3 installed" merely because tests or an isolated build succeeded. Prove SolidWorks loaded the new file from the registered runtime path and showed the new controls.
