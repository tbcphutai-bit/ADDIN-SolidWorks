param(
    [Parameter(Mandatory=$true)][string]$ProductDll,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
if(-not(Test-Path -LiteralPath $ProductDll -PathType Leaf)){throw 'Existing private build required'}
if(-not[IO.Path]::IsPathRooted($OutputDirectory) -or (Test-Path -LiteralPath $OutputDirectory)){throw 'Fresh absolute private output required'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$source=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Run-OwnedInprocessAcceptance.ps1') -Raw
$offset=$source.IndexOf('if($runCode -ne 0 -or $report.Result')
if($offset -lt 0){throw 'Update this regression for the wrapper result contract'}
# Execute the actual final result gate in isolated child shells. This test
# never starts SOLIDWORKS, registers COM or supplies manufacturing evidence.
$gate=$source.Substring($offset)
$sha=(Get-FileHash -LiteralPath $ProductDll -Algorithm SHA256).Hash.ToLowerInvariant()
$cases=@(
    @{Name='startup-failure';Code=1;Result='FAIL';Sha=$null;Product='C:\MISSING-TEST-DO-NOT-READ.dll';ExpectedExit=1},
    @{Name='actual-mismatch';Code=0;Result='PASS';Sha='not-the-loaded-hash';Product=$ProductDll;ExpectedExit=1},
    @{Name='actual-match';Code=0;Result='PASS';Sha=$sha;Product=$ProductDll;ExpectedExit=0}
)
foreach($case in $cases){
    $record=Join-Path $OutputDirectory ($case.Name+'.json')
    @{Result=$case.Result;FunctionalResult=$case.Result;ShutdownResult='TEST';InprocessResult=@{DllSha256=$case.Sha}} |
        ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $record -Encoding UTF8
    $header='$runCode='+$case.Code+"`r`n"+'$ProductDll='+"'"+$case.Product.Replace("'","''")+"'`r`n"+
        '$report=Get-Content -LiteralPath '+"'"+$record.Replace("'","''")+"' -Raw | ConvertFrom-Json`r`n"
    $script=Join-Path $OutputDirectory ($case.Name+'.ps1')
    Set-Content -LiteralPath $script -Value ($header+$gate) -Encoding UTF8
    $logPath=Join-Path $OutputDirectory ($case.Name+'.log')
    $errorPath=Join-Path $OutputDirectory ($case.Name+'-stderr.log')
    # Expected rejection writes stderr. Capture it as a child process so
    # Windows PowerShell's NativeCommandError cannot abort the test runner.
    $shell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $child=Start-Process -FilePath $shell -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$script+'"')) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $logPath -RedirectStandardError $errorPath
    $code=$child.ExitCode;$child.Dispose()
    if($code -ne $case.ExpectedExit){throw "$($case.Name): unexpected exit $code"}
    $log=(Get-Content -LiteralPath $logPath -Raw)+(Get-Content -LiteralPath $errorPath -Raw)
    if($case.Name -eq 'startup-failure' -and $log.Contains('SHA mismatch')){throw 'Startup was mislabeled as loaded binary mismatch'}
    if($case.Name -eq 'actual-mismatch' -and -not $log.Contains('SHA mismatch')){throw 'Actual binary mismatch was not rejected'}
    Write-Output "$($case.Name) PASS"
}
Write-Output 'RESULT 3 wrapper result cases passed; no SOLIDWORKS API.'
