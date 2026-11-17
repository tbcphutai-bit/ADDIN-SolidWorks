param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$InteropDirectory = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist',
    [string]$MSBuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a NEW output directory for isolated builds.' }
New-Item -ItemType Directory -Path $output | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe ('/out:' + (Join-Path $output 'KadaiPlannerTests.exe')) (Join-Path $repo 'tools\KadaiPlannerTests.cs') (Join-Path $repo 'Commands\Kadai\KadaiPlanner.cs')
if ($LASTEXITCODE -ne 0) { throw 'C# planner test compilation failed.' }
& (Join-Path $output 'KadaiPlannerTests.exe') | Tee-Object -FilePath (Join-Path $output 'numeric.txt')
if ($LASTEXITCODE -ne 0) { throw 'C# planner tests failed.' }
foreach ($configuration in @('Debug','Release')) {
    # Suppress the repository's production-copy post-build event. Do not kill SW,
    # register COM, or deploy binaries as a side effect of a Kadai verification build.
    $target = Join-Path $output ($configuration + '\')
    & $MSBuild (Join-Path $repo 'ADDIN.csproj') /t:Build ('/p:Configuration=' + $configuration) /p:Platform=x64 ('/p:OutputPath=' + $target) ('/p:ReferencePath=' + $InteropDirectory) /p:RegisterForComInterop=false /p:PostBuildEvent= /m:1 /nr:false /nologo /v:quiet /fl ('/flp:logfile=' + (Join-Path $output ($configuration + '.log')) + ';verbosity=normal')
    if ($LASTEXITCODE -ne 0) { throw ($configuration + ' x64 build failed.') }
}
'PASS: actual C# planner and Debug/Release x64 builds. SolidWorks CAD tests are separate.'
