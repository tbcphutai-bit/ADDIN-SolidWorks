#Requires -Version 5.1
# CG2.1: TEST-ONLY script. Never runs git pull/fetch, MSBuild, csc or any build.
# Builds are MANUAL via Visual Studio's existing ADDIN.csproj settings:
# Debug|x64 => C:\Tao moi\ADDIN.dll
# Release|x64 => bin\x64\Release\ADDIN.dll; project post-build copies to C:\Tao moi\ADDIN.dll.
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string]$CandidateDll = "C:\Tao moi\ADDIN.dll",
    [string]$OfflineHarnessExe = "",
    [string]$RunnerExe = "",
    [string]$BaselineDll = "",
    [string]$SandboxRoot = "",
    [string[]]$Fixtures = @(),
    [string]$RepeatabilityFixture = "",
    [switch]$RunCad
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$requiredBranch = "feat/check-hole-cg2-part-relations"

function Require-File([string]$Path, [string]$Description) {
    if ([string]::IsNullOrWhiteSpace($Path) -or
        -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description missing: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Assert-SandboxFixture([string]$Root, [string]$Model) {
    $full = [IO.Path]::GetFullPath($Model)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\','/') + '\'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Fixture outside authorized Pack-and-Go sandbox: $Model"
    }
    if ([IO.Path]::GetExtension($full) -ine ".sldasm") {
        throw "Only .SLDASM fixture copies allowed: $Model"
    }
    [void](Require-File $full "Assembly fixture")
    return $full
}

function Invoke-Logged([string]$Name, [string]$Exe, [string[]]$Args,
                       [string]$Folder) {
    $log = Join-Path $Folder ($Name + ".log")
    Write-Host "RUN $Name" -ForegroundColor Cyan
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $Exe @Args 2>&1 | Tee-Object -FilePath $log | Out-Host
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $oldPreference }
    if ($null -eq $code) { $code = 1 }
    Write-Host "$Name EXIT=$code LOG=$log"
    return [int]$code
}

if (-not $RepoRoot) {
    $RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
}
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
$null = Require-File (Join-Path $RepoRoot "ADDIN.csproj") "Visual Studio project"
$logRoot = Join-Path $env:TEMP ("CG2_1_TEST_ONLY_" + (Get-Date -Format "yyyyMMdd_HHmmss"))
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

$status = [ordered]@{
    AUTO_BUILD = "DISABLED"
    SOURCE_HEAD = "NOT_TESTED"
    WORKTREE_STATUS = "NOT_TESTED"
    BUILD_DEBUG_X64 = "MANUAL_NOT_VERIFIED"
    BUILD_RELEASE_X64 = "MANUAL_NOT_VERIFIED"
    CANDIDATE_DLL_PATH = "NOT_TESTED"
    CANDIDATE_DLL_SHA256 = "NOT_TESTED"
    OFFLINE_PATCH_TEST = "NOT_TESTED"
    RUNNER_SELF_TEST = "NOT_TESTED"
    CAD_REGRESSION = "NOT_TESTED"
    CAD_REPEATABILITY = "NOT_TESTED"
    RAW_CSV_BYTE_PARITY = "NOT_TESTED"
    MARKER_GENERATION_PARITY = "NOT_TESTED"
    READY_FOR_MERGE = "NO"
    ERROR = "NONE"
}
$failed = $false

try {
    # Read-only Git inspection. Keep Anti's local class changes intact.
    Push-Location -LiteralPath $RepoRoot
    try {
        $currentBranch = (& git branch --show-current).Trim()
        if ($LASTEXITCODE -ne 0 -or $currentBranch -ne $requiredBranch) {
            throw "Wrong branch. Expected $requiredBranch; actual $currentBranch"
        }
        $head = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw "Unable to read Git HEAD" }
        $status.SOURCE_HEAD = $head
        $changes = @(& git status --porcelain)
        if ($LASTEXITCODE -ne 0) { throw "Unable to read Git worktree status" }
        $status.WORKTREE_STATUS = if ($changes.Count -eq 0) { "CLEAN" } else { "DIRTY_PRESERVED" }
    } finally { Pop-Location }

    $CandidateDll = Require-File $CandidateDll "Manually built ADDIN.dll"
    $status.CANDIDATE_DLL_PATH = $CandidateDll
    $status.CANDIDATE_DLL_SHA256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $CandidateDll).Hash
    Write-Host "This script does NOT build the project. Verify DLL SHA/time matches the manually approved build."

    if ($OfflineHarnessExe) {
        $OfflineHarnessExe = Require-File $OfflineHarnessExe "Manually compiled offline test EXE"
        $code = Invoke-Logged "OFFLINE_PATCH_TEST" $OfflineHarnessExe @($CandidateDll) $logRoot
        $status.OFFLINE_PATCH_TEST = if ($code -eq 0) { "PASS" } else { "FAIL" }
        if ($code -ne 0) { throw "Offline harness FAIL; stop dependent CAD tests" }
    } else {
        Write-Host "OFFLINE_PATCH_TEST=NOT_TESTED (no precompiled harness provided)"
    }

    if ($RunnerExe) {
        $RunnerExe = Require-File $RunnerExe "Manually compiled CG2.1 CAD runner EXE"
        $code = Invoke-Logged "RUNNER_SELF_TEST" $RunnerExe @("--self-test") $logRoot
        $status.RUNNER_SELF_TEST = if ($code -eq 0) { "PASS" } else { "FAIL" }
        if ($code -ne 0) { throw "Runner self-test FAIL; no CAD run" }
    } elseif ($RunCad) {
        throw "RunCad requires -RunnerExe pointing to manually compiled CG2.1 runner"
    } else {
        Write-Host "RUNNER_SELF_TEST=NOT_TESTED (no precompiled runner provided)"
    }

    if ($RunCad) {
        $BaselineDll = Require-File $BaselineDll "CG1 baseline DLL"
        if ([IO.Path]::GetFullPath($BaselineDll) -eq [IO.Path]::GetFullPath($CandidateDll)) {
            throw "CG1 and candidate DLL paths cannot be identical"
        }
        if (-not $SandboxRoot) { throw "RunCad requires approved -SandboxRoot" }
        $SandboxRoot = [IO.Path]::GetFullPath($SandboxRoot)
        $null = Require-File (Join-Path $SandboxRoot "CG1_SANDBOX_OK.txt") "Sandbox approval"
        if ($Fixtures.Count -eq 0) { throw "RunCad requires existing -Fixtures" }
        $models = @()
        foreach ($fixture in $Fixtures) {
            $models += (Assert-SandboxFixture $SandboxRoot $fixture)
        }
        if ($RepeatabilityFixture) {
            $RepeatabilityFixture = Assert-SandboxFixture $SandboxRoot $RepeatabilityFixture
        }
        if (Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue) {
            throw "A SOLIDWORKS session is running. Close it manually; refusing CAD tests."
        }

        $argsForCad = @($BaselineDll, $CandidateDll, $SandboxRoot) + $models
        $code = Invoke-Logged "CAD_REGRESSION" $RunnerExe $argsForCad $logRoot
        $status.CAD_REGRESSION = if ($code -eq 0) { "PASS" } else { "FAIL" }
        # Do not turn a failed CSV byte comparison into a PASS.
        $status.RAW_CSV_BYTE_PARITY = if ($code -eq 0) {
            "PASS"
        } else { "FAIL_OR_NOT_EVALUATED_REVIEW_LOG" }
        if ($code -ne 0) { $failed = $true }

        if ($RepeatabilityFixture) {
            if (Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue) {
                throw "SOLIDWORKS remained open after regression; repeatability refused."
            }
            $code = Invoke-Logged "CAD_REPEATABILITY" $RunnerExe @(
                $BaselineDll, $CandidateDll, $SandboxRoot,
                $RepeatabilityFixture, "--repeatability"
            ) $logRoot
            $status.CAD_REPEATABILITY = if ($code -eq 0) { "PASS" } else { "FAIL" }
            if ($code -ne 0) { $failed = $true }
        }
    } elseif ($Fixtures.Count -gt 0 -or $RepeatabilityFixture) {
        throw "Fixtures supplied without explicit -RunCad approval"
    }
} catch {
    $failed = $true
    $status.ERROR = $_.Exception.Message
    Write-Host ("STOP_OR_LIMITATION=" + $_.Exception.Message) -ForegroundColor Red
} finally {
    $summary = Join-Path $logRoot "CG2_1_TEST_ONLY_SUMMARY.txt"
    $status.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" } |
        Set-Content -LiteralPath $summary -Encoding UTF8
    Get-Content -LiteralPath $summary | ForEach-Object { Write-Host $_ }
    Write-Host "LOCAL_LOG_FOLDER=$logRoot"
    Write-Host "Never upload private CAD, model names or raw CSV logs without permission."
}
if ($failed) { exit 1 }
exit 0
