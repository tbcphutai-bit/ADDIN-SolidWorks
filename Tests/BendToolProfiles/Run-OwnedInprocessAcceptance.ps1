param(
    [Parameter(Mandatory=$true)][string]$Harness,
    [Parameter(Mandatory=$true)][string]$ProductDll,
    [Parameter(Mandatory=$true)][string]$BridgeDll,
    [Parameter(Mandatory=$true)][string]$ReadOnlyFixture,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$SessionExcludedAddin
)
$ErrorActionPreference='Stop'
foreach($file in @($Harness,$ProductDll,$BridgeDll,$ReadOnlyFixture)) {
    if(-not [IO.Path]::IsPathRooted($file) -or -not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Explicit existing input required: $file"}
}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Fresh private output required'}
function Read-StartupState {
    $data=[ordered]@{}
    foreach($rootPath in @('HKCU:\Software\SolidWorks\AddInsStartup','HKLM:\Software\SolidWorks\Addins')) {
        if(-not(Test-Path -LiteralPath $rootPath)){continue}
        foreach($entry in Get-ChildItem -LiteralPath $rootPath | Sort-Object Name) {
            $values=[ordered]@{}
            foreach($name in $entry.GetValueNames() | Sort-Object){$values[$name]=$entry.GetValue($name)}
            $data[$entry.Name]=$values
        }
    }
    return ($data | ConvertTo-Json -Depth 8 -Compress)
}
$before=Read-StartupState
$fixtureSha=(Get-FileHash -LiteralPath $ReadOnlyFixture -Algorithm SHA256).Hash
$oldExclude=$env:ADDIN_NATIVE_EXCLUDE_ADDIN
try {
    # This flag only causes StartOwned to UnloadAddIn in its exact owned PID.
    # No daily startup flag or installed production COM registration is edited.
    $env:ADDIN_NATIVE_EXCLUDE_ADDIN=$SessionExcludedAddin
    & $Harness --inprocess-addin $ProductDll $OutputDirectory $BridgeDll $ReadOnlyFixture none
    $runCode=$LASTEXITCODE
} finally {
    $env:ADDIN_NATIVE_EXCLUDE_ADDIN=$oldExclude
    $after=Read-StartupState
    if(Test-Path -LiteralPath $OutputDirectory) {
        @{StartupSettingsUnchanged=($before -eq $after);FixtureBytesUnchanged=($fixtureSha -eq (Get-FileHash -LiteralPath $ReadOnlyFixture -Algorithm SHA256).Hash)} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'PRIVATE-protection-audit.json') -Encoding UTF8
    }
}
if($before -ne $after){throw 'Startup settings changed during controlled test; investigate instead of claiming PASS'}
$report=Get-Content -LiteralPath (Join-Path $OutputDirectory 'PRIVATE-inprocess-audit.json') -Raw | ConvertFrom-Json
if($report.InprocessResult.DllSha256 -ne (Get-FileHash -LiteralPath $ProductDll -Algorithm SHA256).Hash.ToLowerInvariant()){throw 'Actual in-process DLL SHA mismatch'}
if($runCode -ne 0 -or $report.Result -ne 'PASS'){exit 1}
exit 0
