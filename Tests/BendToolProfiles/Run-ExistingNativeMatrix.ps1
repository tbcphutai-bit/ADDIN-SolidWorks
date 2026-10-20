param(
    [Parameter(Mandatory=$true)][string]$ExistingReport,
    [Parameter(Mandatory=$true)][string]$Sidecar,
    [Parameter(Mandatory=$true)][string]$ProductDll,
    [Parameter(Mandatory=$true)][string]$Harness,
    [Parameter(Mandatory=$true)][string]$MinimumWorkbook,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$PlacementConfiguration
)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Fresh private output required'}
foreach($file in @($ExistingReport,$Sidecar,$ProductDll,$Harness,$MinimumWorkbook)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing input: $file"}
}
$baseline=Get-Content -LiteralPath $ExistingReport -Raw | ConvertFrom-Json
$ids=@($baseline.Cases | ForEach-Object {$_.CaseId})
if($ids.Count -ne 33 -or @($ids | Select-Object -Unique).Count -ne 33){throw 'Exactly the existing 33 fixtures required'}
foreach($case in $baseline.Cases){if(-not(Test-Path -LiteralPath $case.FixturePath -PathType Leaf)){throw "Missing original fixture $($case.CaseId)"}}
New-Item -ItemType Directory -Path $out | Out-Null
$env:ADDIN_BEND_USE_MASTER_PAIRS='1'
$env:ADDIN_BEND_TOOL_DXF_DIRECTORY=$null
$env:ADDIN_BEND_PLACEMENT_CONFIG=$PlacementConfiguration
$env:ADDIN_NATIVE_COMMAND_SCOPE='1'
$overall=0
for($batch=0;$batch -lt 3;$batch++){
    $target=Join-Path $out "batch-$batch"
    $selection=$ids[($batch*11)..($batch*11+10)] -join ','
    & $Harness --excel-existing $ProductDll $target $ExistingReport $MinimumWorkbook $Sidecar $selection > "$out\batch-$batch.log" 2>&1
    if($LASTEXITCODE -ne 0){$overall=1}
    $result=Get-Content -LiteralPath "$target\PRIVATE-native-matrix.json" -Raw | ConvertFrom-Json
    Write-Output "BATCH $batch functional=$($result.FunctionalResult) pass=$($result.PassCount) fail=$($result.FailCount) shutdown=$($result.ShutdownResult)"
    # Keep the 10-second lifecycle verdict. This inter-run safety guard does not
    # change it to PASS and never terminates an existing/user-owned process.
    $ownership=Get-Content -LiteralPath "$target\ownership.json" -Raw | ConvertFrom-Json
    $owned=Get-Process -Id $ownership.Pid -ErrorAction SilentlyContinue
    if($owned -and $owned.StartTime.ToUniversalTime().ToString('o') -eq $ownership.StartUtc){
        if(-not $owned.WaitForExit(30000)){throw 'Owned process still alive; later partitions NOT RUN. No forced termination.'}
    }
}
exit $overall
