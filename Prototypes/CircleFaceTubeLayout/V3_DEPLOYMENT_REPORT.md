# Circle Along Face V3 – Deployment & Verification Report

## 1. Root Cause: Why SOLIDWORKS Was Previously Loading V1 Instead of V3

1. COM Add-in registration in Windows Registry:
   - Key: `HKLM\SOFTWARE\SolidWorks\AddIns\{d1c7f4b2-9a1e-4b5c-b234-a1b2c3d4e5f6}` (`Title = TAI TOOL`)
   - `CLSID\{d1c7f4b2-9a1e-4b5c-b234-a1b2c3d4e5f6}\InprocServer32`: `CodeBase = file:///C:/Tao moi/ADDIN.DLL`
2. Visual Studio (`devenv.exe`) was building `C:\Tao moi\ADDIN` where `Commands/CircleFaceTubeLayout/` still contained the **V1** implementation (`2026-11-10`).
3. In `ADDIN.csproj`, `Debug|x64` sets `<OutputPath>C:\Tao moi\</OutputPath>` and `PostBuildEvent` copies `$(TargetDir)$(TargetName).dll` to `C:\Tao moi\ADDIN.dll`. Building `C:\Tao moi\ADDIN` in Visual Studio previously overwrote `C:\Tao moi\ADDIN.dll` with V1:
   - Old V1 Size: `9,080,320` bytes
   - Old V1 SHA256: `94C467B2AFA0FA891D4965F442354E028F8D911D19B2AC698D9C15952FAA63A6`
   - Old V1 MVID: `141bfd52-9978-4f5c-a888-e2970fcaf15f`
   - Backed up to: `C:\Tao moi\ADDIN.backup_v1_20261118_081955.dll` (and `.pdb`)

---

## 2. Standalone Isolation from `BomTaskPaneControl` & Other Commands

To ensure `Circle Along Face V3` (`Tạo ống 架台`) never interferes with `BomTaskPaneControl`, `PropertyManagerPage`, `Fix Tỉ lệ`, `DIM Kegaki`, or any other command:
1. **`Commands/CircleFaceTubeLayout/CircleFaceTubeLayoutCommandEntry.cs`** (`ADDIN.Commands.CircleFaceTubeLayout.CircleFaceTubeLayoutCommandEntry`):
   - Standalone entry class managing its own open-dialog state (`IsOpen`), exception boundary (`Open(ISldWorks app, IWin32Window owner = null)`), and clean shutdown (`CloseActiveSession()`), with zero dependency on `PropertyManagerPage` or shared `BomTaskPaneControl` state.
2. **`UI/CircleFaceTubeLayoutTaskPaneLauncher.cs`**:
   - Dedicated partial class file holding `btnCircleAlongFace` and `EnsureCircleAlongFaceButton()`, wiring `Click` -> `CircleFaceTubeLayoutCommandEntry.Open(swApp, this)` and `Disposed` -> `CircleFaceTubeLayoutCommandEntry.CloseActiveSession()`.
   - Removed all `btnCircleAlongFace` references from `BomTaskPaneControl.cs` and `UI/BomTaskPaneTheme.cs` (`0` references remaining).
3. Synced V3 `Commands/CircleFaceTubeLayout/` and the isolated launcher into both `C:\Tao moi\ADDIN` and `C:\Tao moi\ADDIN-CircleFaceV3` (`Debug|x64` and `Release|x64` builds pass with `0` errors; `GeometryTests.exe` `/platform:x64 /warnaserror` passes all tests with exit code `0`).

---

## 3. Live SOLIDWORKS 2024 SP5.0 Verification (`6866`)

1. **Visual Studio Rebuild & Runtime Load Proof (`C:\Tao moi\ADDIN.dll`)**:
   - Rebuilt `C:\Tao moi\ADDIN.dll` at `2026-11-18 11:25:27`:
     - Size: `9,116,672` bytes
     - SHA256: `99325F4E1B0407134BDB97B309E3E321EFDE5D8ECDAE95D7287A06718EA9A20A`
     - MVID: `7241bc28-0847-4246-9cf5-a93f99331361`
   - Visual Studio Debugger Output from `SLDWORKS.exe` (`32.5.0`):
     ```
     'SLDWORKS.exe' (CLR v4.0.30319: DefaultDomain): Loaded 'C:\Tao moi\ADDIN.dll'. Symbols loaded.
     [TAI STARTUP] Begin 2026-11-18T11:25:46.8726766+07:00; Assembly=C:\Tao moi\ADDIN.dll; MVID=7241bc28-0847-4246-9cf5-a93f99331361
     [TAI STARTUP] Callback registration ms=32
     [TAI STARTUP] CreateTaskpaneView2 ms=28
     [TAI STARTUP] AddControl + UI constructor ms=603
     [TAI STARTUP] UI Init ms=7
     [TAI STARTUP] ConnectToSW total ms=804
     ```
   - Notice: the `System.Windows.Data Error: 40` binding messages in the Visual Studio Output come from the separate third-party add-in `C:\Users\SGN26\AppData\Local\XTool\XToolPro-Parallel-Test\XToolProParallelTest.dll` (`TaskPaneViewModel`), not from `ADDIN.dll`.

2. **Live V3 Execution Log on `6866` (`%LOCALAPPDATA%\TAI_TOOL\CircleFaceTubeLayout\TUBE_FACE_20261118_112656_276.log`)**:
   - Confirmed `START solver=3` with matching `assembly_path=C:\Tao moi\ADDIN.dll` and `MVID=7241bc28-0847-4246-9cf5-a93f99331361`:
     ```
     2026-11-18T11:26:56.2778897+07:00 START solver=3 addin=ADDIN, Version=1.0.0.0, Culture=neutral, PublicKeyToken=36eed1a140b4715f assembly_path=C:\Tao moi\ADDIN.dll MVID=7241bc28-0847-4246-9cf5-a93f99331361
     2026-11-18T11:26:56.2778897+07:00 SOLIDWORKS revision=32.5.0
     2026-11-18T11:26:56.2778897+07:00 DOCUMENT path=C:\Users\SGN26\Downloads\6866-04-円錐架台-上型-3d 133333.SLDPRT config=ﾃﾞﾌｫﾙﾄ
     2026-11-18T11:26:56.2943403+07:00 RAILS small_mm=817.547001 large_mm=1430.141427 small_pieces=1 large_pieces=1
     ```
   - **Mode 1 (`TangentContact`, `30+1=31`, `D=20 mm`, `Margin=5 mm`)**:
     ```
     PREVIEW_SUMMARY mode=TangentContact count=31 small_pitch_mm=0.000000 large_pitch_mm=46.685411 small_left_mm=0.000000 small_right_mm=0.000000 large_left_mm=5.005831 large_right_mm=5.004990 small_gap_mm=0.004996..0.005726 min_full_mm=0.005023 min_full_pair=21-22 interfering_pairs=0 first_interfering_pair=0-0 sheet_min_mm=0.005791 sheet_clashes=0 sheet_inconclusive=0 lift_mm=2.994870
     PREVIEW_END result=verified
     ```
   - **Mode 2 (`EqualPitch`, `30+1=31`, `D=20 mm`, `Margin=5 mm`)**:
     ```
     PREVIEW_SUMMARY mode=EqualPitch count=31 small_pitch_mm=26.261958 large_pitch_mm=46.681621 small_left_mm=5.006080 small_right_mm=5.004759 large_left_mm=5.004624 large_right_mm=5.004083 small_gap_mm=5.776555..6.342543 min_full_mm=5.776594 min_full_pair=30-31 interfering_pairs=0 first_interfering_pair=0-0 sheet_min_mm=0.019838 sheet_clashes=0 sheet_inconclusive=0 lift_mm=0.197778
     PREVIEW_END result=verified
     ```
   - **Mode 2 (`EqualPitch`, `25+1=26`, `D=20 mm`, `Margin=5 mm`) + `Create 3D Sketch`**:
     ```
     PREVIEW_SUMMARY mode=EqualPitch count=26 small_pitch_mm=31.514349 large_pitch_mm=56.017945 small_left_mm=5.006080 small_right_mm=5.004759 large_left_mm=5.004624 large_right_mm=5.004083 small_gap_mm=10.941848..11.611194 min_full_mm=10.941842 min_full_pair=25-26 interfering_pairs=0 first_interfering_pair=0-0 sheet_min_mm=0.019838 sheet_clashes=0 sheet_inconclusive=0 lift_mm=0.197778
     PREVIEW_END result=verified
     COMMIT sketch=TUBE_AXES_3D_26
     SUCCESS construction_axes=26
     ```
