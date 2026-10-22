param(
 [Parameter(Mandatory=$true)][string]$SourceRoot,
 [Parameter(Mandatory=$true)][string]$InteropDirectory,
 [Parameter(Mandatory=$true)][string]$Compiler,
 [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
# Explicit source-to-EXE verification only. No project/MSBuild/post-build/deploy.
$source=[System.IO.Path]::GetFullPath($SourceRoot)
$output=[System.IO.Path]::GetFullPath($OutputDirectory)
if($output -eq $source){throw 'Use a separate harness output directory'}
[System.IO.Directory]::CreateDirectory($output)|Out-Null
$files=@(
 'Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs',
 'Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs',
 'Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs',
 'Commands/AutoFrameJoint/AutoFrameJointGeometryMath.cs',
 'Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs',
 'UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs'
)
$sources=@($files|ForEach-Object{Join-Path $source $_})
$refs=@('/nologo','/target:exe','/platform:x64','/langversion:7.3','/define:TRACE','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Windows.Forms.dll')
foreach($name in @('SolidWorks.Interop.sldworks.dll','SolidWorks.Interop.swconst.dll','SolidWorks.Interop.swpublished.dll')){
 $library=Join-Path $InteropDirectory $name
 $refs+=('/r:'+$library)
 if([System.IO.Path]::GetFullPath($library) -ne (Join-Path $output $name)){[System.IO.File]::Copy($library,(Join-Path $output $name),$true)}
}
& $Compiler @refs ('/out:'+(Join-Path $output 'ManagedContracts.exe')) (Join-Path $PSScriptRoot 'ManagedApplyHarness.cs.txt') @sources
if($LASTEXITCODE -ne 0){throw 'Detached managed harness compilation failed'}
& (Join-Path $output 'ManagedContracts.exe') (Join-Path $output 'managed-results')
if($LASTEXITCODE -ne 0){throw 'Managed contract controls failed'}
& $Compiler @refs ('/out:'+(Join-Path $output 'NativeApply.exe')) (Join-Path $PSScriptRoot 'NativeApplyHarness.cs.txt') (Join-Path $PSScriptRoot 'HarnessConnection.cs.txt') @sources
if($LASTEXITCODE -ne 0){throw 'Detached native harness compilation failed'}
$manifest=@($files|ForEach-Object{[PSCustomObject]@{file=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $source $_) -Algorithm SHA256).Hash.ToLowerInvariant()}})
$manifest|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $output 'source-manifest.json') -Encoding utf8
Write-Output 'Detached EXEs compiled; managed controls passed. Native API harness was NOT executed by this script.'
