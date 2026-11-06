param(
 [Parameter(Mandatory=$true)][string]$CompilerPath,
 [Parameter(Mandatory=$true)][string]$InteropDirectory,
 [string]$SourceRoot=(Resolve-Path (Join-Path $PSScriptRoot '../../../../../')).Path,
 [string]$OutputDirectory=(Join-Path $env:TEMP 'AFJ_R52_SmoothFinishManagedControls')
)
$ErrorActionPreference='Stop'
# Detached explicit-source harness only. No project build, SOLIDWORKS activation or deployment.
if(!(Test-Path -LiteralPath $CompilerPath -PathType Leaf)){throw 'C# compiler not found'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$references=@('/nologo','/target:exe','/platform:x64','/langversion:7.3','/define:TRACE','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Windows.Forms.dll')
foreach($dependency in @('SolidWorks.Interop.sldworks.dll','SolidWorks.Interop.swconst.dll','SolidWorks.Interop.swpublished.dll')){
 $path=Join-Path $InteropDirectory $dependency
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw ('Missing Interop: '+$dependency)}
 $references+=('/r:'+$path)
 if([IO.Path]::GetFullPath($path) -ne [IO.Path]::GetFullPath((Join-Path $OutputDirectory $dependency))){Copy-Item -LiteralPath $path -Destination (Join-Path $OutputDirectory $dependency) -Force}
}
$sources=@((Join-Path $PSScriptRoot 'SmoothFinishHarness.cs'),(Join-Path $SourceRoot 'UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs'))
foreach($name in @('AutoFrameJointCommandEntry.cs','AutoFrameJointGhostSessionManager.cs','AutoFrameJointGhostPreview.cs','AutoFrameJointGeometryMath.cs','AutoFrameJointReadOnlyProbe.cs')){$sources+=Join-Path $SourceRoot ('Commands/AutoFrameJoint/'+$name)}
$executable=Join-Path $OutputDirectory 'AFJManagedControls.exe'
& $CompilerPath @references ('/out:'+$executable) @sources
if($LASTEXITCODE -ne 0){throw 'Detached compilation failed'}
& $executable (Join-Path $OutputDirectory 'controls')
if($LASTEXITCODE -ne 0){throw 'Managed controls failed; see controls/managed_flow_results.json'}
