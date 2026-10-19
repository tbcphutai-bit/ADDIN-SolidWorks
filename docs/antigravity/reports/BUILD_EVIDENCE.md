# CHECK HOLE CG1 — BUILD EVIDENCE

> Environment: Windows, .NET Framework 4.8, C# 7.3, MSBuild 18.10.1-1.26427.6
> Visual Studio: Microsoft Visual Studio Community 18
> Target Project: `ADDIN.csproj`
> Branch: `feat/check-hole-connection-groups-cg1`
> Commit under test: `dc39fa71dd3140a9e7b683bbb95617d0afb66254` (Complete CG1 implementation commit)

---

## 1. Release|x64 Build

### Command
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" ADDIN.csproj /p:Configuration=Release /p:Platform=x64 /v:normal
```

### Log Output
```text
MSBuild version 18.10.1-1.26427.6+3cd27c13e for .NET Framework
Build started 2026/10/19 11:55:46.

Project "C:\Tao moi\ADDIN\ADDIN.csproj" on node 1 (default targets).
MainResourcesGeneration:
  Skipping target "MainResourcesGeneration" because all output files are up-to-date with respect to the input files.
CoreResGen:
  Task assembly loaded from 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Microsoft.Build.dll'.
GenerateTargetFrameworkMonikerAttribute:
  Skipping target "GenerateTargetFrameworkMonikerAttribute" because all output files are up-to-date.
CoreCompile:
  Skipping target "CoreCompile" because all output files are up-to-date.
_CopyOutOfDateSourceItemsToOutputDirectory:
  Skipping target "_CopyOutOfDateSourceItemsToOutputDirectory" because all output files are up-to-date.
CopyFilesToOutputDirectory:
  ADDIN -> C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll
PostBuildEvent:
  copy /Y "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll" "C:\Tao moi\ADDIN.dll" & copy /Y "C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.pdb" "C:\Tao moi\ADDIN.pdb" 2>nul & set errorlevel=0
          1 file(s) copied.
Done Building Project "C:\Tao moi\ADDIN\ADDIN.csproj" (default targets).

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.25
```

### Metrics
- **Exit Code:** 0
- **Warnings:** 0
- **Errors:** 0
- **Primary Binary:** `C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll`

---

## 2. Debug|x64 Build

### Command
```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" ADDIN.csproj /p:Configuration=Debug /p:Platform=x64 /v:normal
```

### Log Output
```text
MSBuild version 18.10.1-1.26427.6+3cd27c13e for .NET Framework
Build started 2026/10/19 11:56:06.

Project "C:\Tao moi\ADDIN\ADDIN.csproj" on node 1 (default targets).
MainResourcesGeneration:
  Skipping target "MainResourcesGeneration" because all output files are up-to-date with respect to the input files.
CoreResGen:
  Task assembly loaded from 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Microsoft.Build.dll'.
GenerateTargetFrameworkMonikerAttribute:
  Skipping target "GenerateTargetFrameworkMonikerAttribute" because all output files are up-to-date.
CoreCompile:
  Skipping target "CoreCompile" because all output files are up-to-date.
_CopyOutOfDateSourceItemsToOutputDirectory:
  Skipping target "_CopyOutOfDateSourceItemsToOutputDirectory" because all output files are up-to-date.
CopyFilesToOutputDirectory:
  Copying file from "C:\Tao moi\ADDIN\obj\x64\Debug\ADDIN.dll" to "C:\Tao moi\ADDIN.dll".
  ADDIN -> C:\Tao moi\ADDIN.dll
  Copying file from "C:\Tao moi\ADDIN\obj\x64\Debug\ADDIN.pdb" to "C:\Tao moi\ADDIN.pdb".
PostBuildEvent:
  copy /Y "C:\Tao moi\ADDIN.dll" "C:\Tao moi\ADDIN.dll" & copy /Y "C:\Tao moi\ADDIN.pdb" "C:\Tao moi\ADDIN.pdb" 2>nul & set errorlevel=0
          0 file(s) copied.
Done Building Project "C:\Tao moi\ADDIN\ADDIN.csproj" (default targets).

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.35
```

### Metrics
- **Exit Code:** 0
- **Warnings:** 0
- **Errors:** 0
- **Primary Binary:** `C:\Tao moi\ADDIN.dll`

*(Note: During build, if an external SolidWorks process is actively locking `C:\Tao moi\ADDIN.dll`, closing the orphan instance or building with `/p:OutputPath=bin\x64\Debug\` resolves file lock contention without source changes).*
