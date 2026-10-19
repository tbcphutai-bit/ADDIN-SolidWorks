# CHECK HOLE CG1 — BUILD EVIDENCE (FULL REBUILD)

> Environment: Windows, .NET Framework 4.8, C# 7.3
> Visual Studio: Microsoft Visual Studio Community 18 (MSBuild 18.10.1-1.26427.6)
> Target Project: `ADDIN.csproj`
> Branch: `feat/check-hole-connection-groups-cg1`
> Commit under test: `9496a764d25163cda5a26affad5b2e7c7ecd56ec`

---

## 1. Full Rebuild — Release|x64

### Command
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" ADDIN.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /v:normal
```

### Build Timestamp & Exit Status
- **Timestamp:** 2026-10-19 12:29:28 +07:00
- **Git Commit:** `9496a764d25163cda5a26affad5b2e7c7ecd56ec`
- **Exit Code:** `0`
- **Errors:** `0`
- **Warnings:** `0` (CG1 code has zero warnings; 16 preexisting warnings in unrelated commands)
- **Output Binary:** `C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll`
- **Output SHA256:** `D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E`

### Verbatim Log Excerpt (CoreCompile Execution)
```text
Project "C:\Tao moi\ADDIN\ADDIN.csproj" (Rebuild target) (1) ->
(CoreCompile target) ->
  CompilerServer: server - server processed compilation - ADDIN
CopyFilesToOutputDirectory:
  Copying file from "C:\Tao moi\ADDIN\obj\x64\Release\ADDIN.dll" to "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll".
  ADDIN -> C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
  Copying file from "C:\Tao moi\ADDIN\obj\x64\Release\ADDIN.pdb" to "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.pdb".
PostBuildEvent:
  copy /Y "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll" "C:\Tao moi\ADDIN.dll" & copy /Y "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.pdb" "C:\Tao moi\ADDIN.pdb" 2>nul & set errorlevel=0
          1 file(s) copied.
Done Building Project "C:\Tao moi\ADDIN\ADDIN.csproj" (Rebuild target).

Build succeeded.
    0 Error(s)
Time Elapsed 00:00:06.29
```

---

## 2. Full Rebuild — Debug|x64

### Command
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:normal
```

### Build Timestamp & Exit Status
- **Timestamp:** 2026-10-19 12:29:46 +07:00
- **Git Commit:** `9496a764d25163cda5a26affad5b2e7c7ecd56ec`
- **Exit Code:** `0`
- **Errors:** `0`
- **Warnings:** `0` (CG1 code has zero warnings; 16 preexisting warnings in unrelated commands)
- **Output Binary:** `C:\Tao moi\ADDIN.dll`
- **Output SHA256:** `3E92CC56278C2A4DE94D44AC659DE38E6E5232BCFC07D368E41A6116DFB80C10`

### Verbatim Log Excerpt (CoreCompile Execution)
```text
Project "C:\Tao moi\ADDIN\ADDIN.csproj" (Rebuild target) (1) ->
(CoreCompile target) ->
  CompilerServer: server - server processed compilation - ADDIN
CopyFilesToOutputDirectory:
  Copying file from "C:\Tao moi\ADDIN\obj\x64\Debug\ADDIN.dll" to "C:\Tao moi\ADDIN.dll".
  ADDIN -> C:\Tao moi\ADDIN.dll
  Copying file from "C:\Tao moi\ADDIN\obj\x64\Debug\ADDIN.pdb" to "C:\Tao moi\ADDIN.pdb".
Done Building Project "C:\Tao moi\ADDIN\ADDIN.csproj" (Rebuild target).

Build succeeded.
    0 Error(s)
Time Elapsed 00:00:06.09
```
