# CHECK HOLE CG1 — BUILD EVIDENCE (FULL REBUILD)

> Environment: Windows, .NET Framework 4.8, C# 7.3
> Visual Studio: Microsoft Visual Studio Community 18 (MSBuild 18.10.1-1.26427.6)
> Target Project: `ADDIN.csproj`
> Branch: `feat/check-hole-connection-groups-cg1`
> Commit under test: `29ff173e957b2adf8ee709cb2fce993bab82455c`

---

## 1. Compiler Warning Analysis & Reconciliation (Review #3)

To resolve any ambiguity regarding warning counts noted in Review #3:
- **CG1 Code Warnings:** `0` (Zero warnings across all CG1 files: `CheckAssemblyHole.ConnectionGroups.cs`, `CheckHoleResultsForm.cs` CG1 logic).
- **Preexisting Codebase Warnings:** `16` total reported by MSBuild (consisting of `8` unique preexisting warnings emitted twice due to MSBuild's two-pass WPF compilation: temporary markup pass `ADDIN_*_wpftmp.csproj` + main compilation `ADDIN.csproj`).
- **Compiler Errors:** `0` (Zero errors).

### Breakdown of the 8 Preexisting Warnings
1. `Commands\LenhDimCanhSongSong.Envelope.cs(78,18)`: CS0219 (variable `brokenViewWasSuspended` assigned but not used)
2. `Commands\BomDrawingReference.cs(6,23)`: CS0649 (field `BomDrawingReference.ModelPath` never assigned)
3. `Commands\BomDrawingReference.cs(7,23)`: CS0649 (field `BomDrawingReference.DrawingPath` never assigned)
4. `Commands\BomDrawingReference.cs(8,23)`: CS0649 (field `BomDrawingReference.Configuration` never assigned)
5. `Commands\LenhDimCanhSongSong.Envelope.cs(21,40)`: CS0649 (field `PlannedDisplay.BrokenArc` never assigned)
6. `Commands\CheckAssemblyHole.cs(129,25)`: CS0649 (field `RunProfile.MarkerGraphicsMs` never assigned)
7. `Commands\CheckAssemblyHole.cs(132,25)`: CS0649 (field `RunProfile.RebuildGraphicsMs` never assigned)
8. `Commands\CheckAssemblyHole.cs(149,25)`: CS0649 (field `RunProfile.GraphicsRefreshCalls` never assigned)

---

## 2. Full Rebuild — Release|x64

### Command
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Tao moi\ADDIN\ADDIN.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=x64 /v:minimal
```

### Build Timestamp & Exit Status
- **Timestamp:** 2026-10-19 13:24:15 +07:00
- **Exit Code:** `0`
- **Errors:** `0`
- **Warnings:** `16` total (CG1 code: `0`, Preexisting: `16`)
- **Output Binary:** `C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll`
- **Output Binary SHA256:** `D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E`
- **Module Version ID (MVID):** `456f9ade-ac27-4395-854f-857ad2521fd0`

### Verbatim Log Excerpt
```text
MSBuild のバージョン 18.10.1-1.26427.6+3cd27c13e (.NET Framework)

C:\Tao moi\ADDIN\Commands\LenhDimCanhSongSong.Envelope.cs(78,18): warning CS0219: 変数 'brokenViewWasSuspended' は割り当てられていますが、その値は使用されていません [C:\Tao moi\ADDIN\ADDIN_0w2uj5f5_wpftmp.csproj]
C:\Tao moi\ADDIN\Commands\BomDrawingReference.cs(7,23): warning CS0649: フィールド 'BomDrawingReference.DrawingPath' は割り当てられません。常に既定値 null を使用します [C:\Tao moi\ADDIN\ADDIN_0w2uj5f5_wpftmp.csproj]
C:\Tao moi\ADDIN\Commands\CheckAssemblyHole.cs(132,25): warning CS0649: フィールド 'CheckAssemblyHole.RunProfile.RebuildGraphicsMs' は割り当てられません。常に既定値 0 を使用します [C:\Tao moi\ADDIN\ADDIN_0w2uj5f5_wpftmp.csproj]
C:\Tao moi\ADDIN\Commands\CheckAssemblyHole.cs(129,25): warning CS0649: フィールド 'CheckAssemblyHole.RunProfile.MarkerGraphicsMs' は割り当てられません。常に既定値 0 を使用します [C:\Tao moi\ADDIN\ADDIN_0w2uj5f5_wpftmp.csproj]
C:\Tao moi\ADDIN\Commands\LenhDimCanhSongSong.Envelope.cs(21,40): warning CS0649: フィールド 'LenhDimCanhSongSong.PlannedDisplay.BrokenArc' は割り当てられません。常に既定値 null を使用します [C:\Tao moi\ADDIN\ADDIN_0w2uj5f5_wpftmp.csproj]
C:\Tao moi\ADDIN\Commands\BomDrawingReference.cs(8,23): warning CS0649: フィールド 'BomDrawingReference.Configuration' は割り当てられません。常に既定値 null を使用します [C:\Tao moi\ADDIN\ADDIN_0w2uj5f5_wpftmp.csproj]
C:\Tao moi\ADDIN\Commands\CheckAssemblyHole.cs(149,25): warning CS0649: フィールド 'CheckAssemblyHole.RunProfile.GraphicsRefreshCalls' は割り当てられません。常に既定値 0 を使用します [C:\Tao moi\ADDIN\ADDIN_0w2uj5f5_wpftmp.csproj]
C:\Tao moi\ADDIN\Commands\BomDrawingReference.cs(6,23): warning CS0649: フィールド 'BomDrawingReference.ModelPath' は割り当てられません。常に既定値 null を使用します [C:\Tao moi\ADDIN\ADDIN_0w2uj5f5_wpftmp.csproj]
C:\Tao moi\ADDIN\Commands\LenhDimCanhSongSong.Envelope.cs(78,18): warning CS0219: 変数 'brokenViewWasSuspended' は割り当てられていますが、その値は使用されていません [C:\Tao moi\ADDIN\ADDIN.csproj]
C:\Tao moi\ADDIN\Commands\CheckAssemblyHole.cs(132,25): warning CS0649: フィールド 'CheckAssemblyHole.RunProfile.RebuildGraphicsMs' は割り当てられません。常に既定値 0 を使用します [C:\Tao moi\ADDIN\ADDIN.csproj]
C:\Tao moi\ADDIN\Commands\BomDrawingReference.cs(6,23): warning CS0649: フィールド 'BomDrawingReference.ModelPath' は割り当てられません。常に既定値 null を使用します [C:\Tao moi\ADDIN\ADDIN.csproj]
C:\Tao moi\ADDIN\Commands\BomDrawingReference.cs(8,23): warning CS0649: フィールド 'BomDrawingReference.Configuration' は割り当てられません。常に既定値 null を使用します [C:\Tao moi\ADDIN\ADDIN.csproj]
C:\Tao moi\ADDIN\Commands\BomDrawingReference.cs(7,23): warning CS0649: フィールド 'BomDrawingReference.DrawingPath' は割り当てられません。常に既定値 null を使用します [C:\Tao moi\ADDIN\ADDIN.csproj]
C:\Tao moi\ADDIN\Commands\CheckAssemblyHole.cs(129,25): warning CS0649: フィールド 'CheckAssemblyHole.RunProfile.MarkerGraphicsMs' は割り当てられません。常に既定値 0 を使用します [C:\Tao moi\ADDIN\ADDIN.csproj]
C:\Tao moi\ADDIN\Commands\CheckAssemblyHole.cs(149,25): warning CS0649: フィールド 'CheckAssemblyHole.RunProfile.GraphicsRefreshCalls' は割り当てられません。常に既定値 0 を使用します [C:\Tao moi\ADDIN\ADDIN.csproj]
C:\Tao moi\ADDIN\Commands\LenhDimCanhSongSong.Envelope.cs(21,40): warning CS0649: フィールド 'LenhDimCanhSongSong.PlannedDisplay.BrokenArc' は割り当てられません。常に既定値 null を使用します [C:\Tao moi\ADDIN\ADDIN.csproj]
  ADDIN -> C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
          1 個のファイルをコピーしました。
          1 個のファイルをコピーしました。
```

---

## 3. Full Rebuild — Debug|x64

### Command
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Tao moi\ADDIN\ADDIN.csproj" /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:minimal
```

### Build Timestamp & Exit Status
- **Timestamp:** 2026-10-19 13:24:32 +07:00
- **Exit Code:** `0`
- **Errors:** `0`
- **Warnings:** `16` total (CG1 code: `0`, Preexisting: `16`)
- **Output Binary:** `C:\Tao moi\ADDIN.dll`
- **Output Binary SHA256:** `3E92CC56278C2A4DE94D44AC659DE38E6E5232BCFC07D368E41A6116DFB80C10`
