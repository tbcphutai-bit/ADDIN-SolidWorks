param(
    [Parameter(Mandatory=$true)][string]$Sidecar,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$PartTemplate,
    [Parameter(Mandatory=$true)][string]$MinimumWorkbook,
    [string]$PlacementConfiguration,
    [string]$CaseIds,
    [ValidateSet('Debug','Release')][string]$Configuration='Release',
    [string]$MSBuild='C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe'
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Use a new private output directory. Existing CAD/results are never replaced.'}
foreach($source in @($Sidecar,$PartTemplate,$MinimumWorkbook,$MSBuild)){
    if(-not (Test-Path -LiteralPath $source -PathType Leaf)){throw "Required input missing: $source"}
}
New-Item -ItemType Directory -Path $out | Out-Null
$build=Join-Path $out 'build'
$common=@('/t:Rebuild','/nologo','/v:minimal',"/p:Configuration=$Configuration",'/p:Platform=x64','/p:UseSharedCompilation=false')
if($Configuration -eq 'Debug'){$common+=@('/p:DefineConstants=DEBUG%3BTRACE','/p:Optimize=false')}else{$common+=@('/p:DefineConstants=TRACE','/p:Optimize=true')}
$projects=@{product='ADDIN.csproj';input='Tests\BendToolProfiles\ExcelNativeMatrix.Tests.csproj';native='Tests\BendToolProfiles\NativeAssemblyBatch.Validation.csproj'}
Push-Location -LiteralPath $repo
try {
    foreach($target in @('input','product','native')){
        $arguments=@($projects[$target])+$common+@("/p:OutputPath=$build\$target\","/p:BaseIntermediateOutputPath=$build\obj-$target\")
        if($target -eq 'product'){$arguments+=@('/p:KillSolidWorksOnBuild=false','/p:PreBuildEvent=','/p:PostBuildEvent=','/p:RunPostBuildEvent=Never','/p:RegisterForComInterop=false')}
        & $MSBuild @arguments > "$out\build-$target.log" 2>&1
        if($LASTEXITCODE -ne 0){throw "Build $target failed; see build log"}
    }
    & "$build\input\ExcelNativeMatrix.Tests.exe" --validate $Sidecar > "$out\input-validation.log" 2>&1
    if($LASTEXITCODE -ne 0){throw 'Matrix source/interpretation validation failed before starting SOLIDWORKS'}
    # Only the harness's owned native process is touched; never unregister or
    # deploy an add-in, close the user's SW, or change original CAD/Excel files.
    $env:ADDIN_BEND_USE_MASTER_PAIRS='1'
    $env:ADDIN_BEND_TOOL_DXF_DIRECTORY=$null
    if($PlacementConfiguration){$env:ADDIN_BEND_PLACEMENT_CONFIG=[IO.Path]::GetFullPath($PlacementConfiguration)}
    $env:ADDIN_NATIVE_COMMAND_SCOPE='1'
    $nativeArguments=@('--excel-matrix',"$build\product\ADDIN.dll","$out\fixtures",$PartTemplate,$MinimumWorkbook,$Sidecar)
    if($CaseIds){$nativeArguments+=$CaseIds}
    & "$build\native\NativeAssemblyBatch.Validation.exe" @nativeArguments > "$out\native.log" 2>&1
    $nativeCode=$LASTEXITCODE
    Write-Output "Native harness exit code: $nativeCode. Functional cases and shutdown acceptance are separate in fixtures\PRIVATE-native-matrix.json."
    exit $nativeCode
} finally {Pop-Location}
