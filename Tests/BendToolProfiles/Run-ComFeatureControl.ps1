param(
    [Parameter(Mandatory=$true)][ValidateSet('A','B')][string]$Mode,
    [Parameter(Mandatory=$true)][string]$Harness,
    [Parameter(Mandatory=$true)][string]$ProductDll,
    [Parameter(Mandatory=$true)][string]$BridgeDll,
    [Parameter(Mandatory=$true)][string]$Manifest,
    [Parameter(Mandatory=$true)][string]$OutputRoot,
    [Parameter(Mandatory=$true)][string]$SolidWorksExe,
    [Parameter(Mandatory=$true)][string]$ProcDump,
    [Parameter(Mandatory=$true)][string]$SessionExcludedAddin,
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedProductSha256
)
$ErrorActionPreference='Stop'
foreach($path in @($Harness,$ProductDll,$BridgeDll,$Manifest,$SolidWorksExe,$ProcDump,$SessionExcludedAddin)) {
    if(-not [IO.Path]::IsPathRooted($path) -or -not(Test-Path -LiteralPath $path -PathType Leaf)){throw 'Explicit existing private inputs required'}
}
if(-not [IO.Path]::IsPathRooted($OutputRoot)){throw 'Absolute private output root required'}
if((Get-FileHash -LiteralPath $ProductDll -Algorithm SHA256).Hash -ne $ExpectedProductSha256){throw 'Unexpected product binary'}
$inputData=Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
if(@($inputData.Cases.CaseId) -join ',' -ne 'XL-02,XL-17,XL-19,PART-3,ASM-SMALL'){throw 'Existing protected representative manifest required'}
function Sources-Unchanged {
    foreach($source in $inputData.ProtectedSha256.PSObject.Properties) {
        if((Get-FileHash -LiteralPath $source.Name -Algorithm SHA256).Hash -ne $source.Value){return $false}
    }
    return $true
}
function Startup-Snapshot {
    $data=[ordered]@{}
    foreach($rootPath in @('HKCU:\Software\SolidWorks\AddInsStartup','HKLM:\Software\SolidWorks\Addins')) {
        foreach($entry in Get-ChildItem -LiteralPath $rootPath | Sort-Object Name) {
            $values=[ordered]@{};foreach($name in $entry.GetValueNames() | Sort-Object){$values[$name]=$entry.GetValue($name)}
            $data[$entry.Name]=$values
        }
    }
    return ($data | ConvertTo-Json -Depth 8 -Compress)
}
if(-not (Sources-Unchanged)){throw 'Protected input missing or stale'}
[IO.Directory]::CreateDirectory($OutputRoot) | Out-Null
$runDirectory=Join-Path $OutputRoot ('control-'+$Mode)
# One claim per arm; a failure does not silently retry the native experiment.
$claim=[IO.File]::Open((Join-Path $OutputRoot ($Mode+'.claimed')),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
$claim.Dispose()
$before=Startup-Snapshot
$before | Set-Content -LiteralPath (Join-Path $OutputRoot ($Mode+'-PRIVATE-startup-before.json')) -Encoding UTF8
$settings=@{
    ADDIN_ACCEPTANCE_COM_CONTROL=$Mode;ADDIN_ACCEPTANCE_CLOSE_REPRO=$null;ADDIN_ACCEPTANCE_PASSED_XL02_AUDIT=$null;
    ADDIN_NATIVE_HEAP_DUMP_ONLY='1';ADDIN_NATIVE_COMMAND_SCOPE='1';ADDIN_NATIVE_SKIP_STARTUP_READY=$null;
    ADDIN_BEND_USE_MASTER_PAIRS='1';ADDIN_BEND_CLOSE_TRACE='1';ADDIN_NATIVE_EXCLUDE_ADDIN=$SessionExcludedAddin;
    ADDIN_NATIVE_PROCDUMP=$ProcDump;ADDIN_NATIVE_SOLIDWORKS_EXE=$SolidWorksExe
}
$old=@{};foreach($key in $settings.Keys){$old[$key]=[Environment]::GetEnvironmentVariable($key);[Environment]::SetEnvironmentVariable($key,$settings[$key])}
$code=1
try {
    $fixture=if($Mode -eq 'A'){$inputData.Cases[2].Path}else{$inputData.Cases[1].Path}
    & $Harness --inprocess-addin $ProductDll $runDirectory $BridgeDll $fixture none $Manifest > (Join-Path $OutputRoot ($Mode+'-PRIVATE-native.log')) 2>&1
    $code=$LASTEXITCODE
} finally {
    foreach($key in $settings.Keys){[Environment]::SetEnvironmentVariable($key,$old[$key])}
    $after=Startup-Snapshot
    $after | Set-Content -LiteralPath (Join-Path $OutputRoot ($Mode+'-PRIVATE-startup-after.json')) -Encoding UTF8
    $unchanged=Sources-Unchanged
    @{Mode=$Mode;NativeExit=$code;StartupSettingsUnchanged=($before -eq $after);ProtectedSourcesUnchanged=$unchanged;ProductSha256=$ExpectedProductSha256} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputRoot ($Mode+'-PRIVATE-protection.json')) -Encoding UTF8
}
$audit=Get-Content -LiteralPath (Join-Path $runDirectory 'PRIVATE-inprocess-audit.json') -Raw | ConvertFrom-Json
if($code -ne 0 -or $audit.Result -ne 'PASS' -or $before -ne $after -or -not $unchanged){Write-Output "COM control $Mode FAIL; no retry";exit 1}
if($Mode -eq 'A') {
    if($audit.InprocessResult.NoProductControl -ne $true -or $audit.InprocessResult.ProductAssemblyLoaded -ne $false){throw 'Control A loaded product'}
} elseif($audit.InprocessResult.DllSha256 -ne $ExpectedProductSha256){throw 'Control B did not load exact product DLL'}
Write-Output "COM control $Mode PASS, shutdown=$($audit.ShutdownResult)"
exit 0
