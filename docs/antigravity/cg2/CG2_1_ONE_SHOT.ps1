#Requires -Version 5.1
# CG2.1 ChatGPT-authored one-shot validation. Runs on Windows with VS and SOLIDWORKS.
# Always sandbox CAD; never changes C# source, existing CAD, markers, or main.
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string]$BaselineDll = "",
    [string]$SandboxRoot = "",
    [string[]]$Fixtures = @(),
    [switch]$RunCad,
    [string]$RepeatabilityFixture = ""
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$branch = "feat/check-hole-cg2-part-relations"

function Require-File([string]$Value, [string]$Title) {
    if (-not $Value -or -not (Test-Path -LiteralPath $Value -PathType Leaf)) {
        throw "$Title missing: $Value"
    }
    return (Resolve-Path -LiteralPath $Value).Path
}
function Find-MsBuild {
    $command = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $pf = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
    if ($pf) {
        $vw = Join-Path $pf "Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path -LiteralPath $vw) {
            $matches = @(& $vw -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe")
            foreach ($match in $matches) {
                if (Test-Path -LiteralPath $match -PathType Leaf) { return $match }
            }
        }
    }
    throw "Visual Studio MSBuild.exe not found; open Developer PowerShell."
}
function Find-Interop([string]$Name) {
    $paths = @(
        (Join-Path (Join-Path $env:USERPROFILE "Downloads") $Name),
        (Join-Path "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS" $Name)
    )
    foreach ($p in $paths) {
        if (Test-Path -LiteralPath $p -PathType Leaf) { return $p }
    }
    throw "SOLIDWORKS interop DLL missing: $Name. Check ADDIN.csproj HintPath."
}
function Run-Tool([string]$Name, [string]$Executable, [string[]]$Arguments, [string]$Logs) {
    $log = Join-Path $Logs ($Name + ".log")
    Write-Host "RUN $Name" -ForegroundColor Cyan
    $old = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $Executable @Arguments 2>&1 | Tee-Object -FilePath $log
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $old }
    if ($null -eq $code) { $code = 1 }
    Write-Host "$Name EXIT=$code LOG=$log"
    return [int]$code
}
function Validate-Fixture([string]$Root, [string]$Model) {
    $path = [IO.Path]::GetFullPath($Model)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd('\','/') + '\'
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Fixture not inside consented sandbox: $Model"
    }
    if ([IO.Path]::GetExtension($path) -ine ".sldasm") { throw "Not a SLDASM fixture: $Model" }
    [void](Require-File $path "CAD fixture")
    return $path
}
if (-not $RepoRoot) {
    $RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
}
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
$project = Require-File (Join-Path $RepoRoot "ADDIN.csproj") "Project"
$src = Join-Path $RepoRoot "docs\antigravity\cg2"
$models = @()
if ($RunCad) {
    $BaselineDll = Require-File $BaselineDll "CG1 baseline DLL"
    if (-not $SandboxRoot) { throw "RunCad requires -SandboxRoot" }
    $SandboxRoot = [IO.Path]::GetFullPath($SandboxRoot)
    [void](Require-File (Join-Path $SandboxRoot "CG1_SANDBOX_OK.txt") "Sandbox approval file")
    if ($Fixtures.Count -eq 0) { throw "RunCad requires -Fixtures." }
    foreach ($fixture in $Fixtures) { $models += (Validate-Fixture $SandboxRoot $fixture) }
    if ($RepeatabilityFixture) {
        $RepeatabilityFixture = Validate-Fixture $SandboxRoot $RepeatabilityFixture
    }
    if (Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue) {
        throw "User SOLIDWORKS session exists; close it manually before CAD test."
    }
} elseif ($Fixtures.Count -gt 0 -or $RepeatabilityFixture) {
    throw "CAD fixture supplied without explicit -RunCad switch."
}

$logRoot = Join-Path $env:TEMP ("CG2_1_ONE_SHOT_" + (Get-Date -Format "yyyyMMdd_HHmmss"))
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$status = [ordered]@{
    SOURCE_HEAD = "NOT_TESTED"
    BUILD_RELEASE_X64 = "NOT_TESTED"
    BUILD_DEBUG_X64 = "NOT_TESTED"
    OFFLINE_PATCH_TEST = "NOT_TESTED"
    RUNNER_SELF_TEST = "NOT_TESTED"
    CAD_REGRESSION = "NOT_TESTED"
    CAD_REPEATABILITY = "NOT_TESTED"
    RAW_CSV_BYTE_PARITY = "NOT_TESTED"
    MARKER_GENERATION_PARITY = "NOT_TESTED"
    CANDIDATE_DLL_SHA256 = "NOT_TESTED"
    C_SHARP_EDITED_BY_ANTI = "NO"
    READY_FOR_MERGE = "NO"
    ERROR = "NONE"
}
$failed = $false
try {
    Push-Location -LiteralPath $RepoRoot
    try {
        $current = (& git branch --show-current).Trim()
        if ($LASTEXITCODE -ne 0 -or $current -ne $branch) { throw "Wrong Git branch: $current" }
        $dirty = @(& git status --porcelain)
        if ($LASTEXITCODE -ne 0 -or $dirty.Count -gt 0) {
            throw "Working tree has modifications. Preserve them; do not reset."
        }
        & git fetch origin $branch
        if ($LASTEXITCODE -ne 0) { throw "Git fetch failed" }
        & git pull --ff-only origin $branch
        if ($LASTEXITCODE -ne 0) { throw "Fast-forward pull failed" }
        $sha = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or $sha.Length -ne 40) { throw "Cannot verify SHA" }
        $status.SOURCE_HEAD = $sha
    } finally { Pop-Location }

    $msbuild = Find-MsBuild
    $csc = Require-File (Join-Path (Split-Path $msbuild -Parent) "Roslyn\csc.exe") "C# compiler"
    $sw = Find-Interop "SolidWorks.Interop.sldworks.dll"
    $swconst = Find-Interop "SolidWorks.Interop.swconst.dll"
    $release = Join-Path $logRoot "Release"
    $debug = Join-Path $logRoot "Debug"
    New-Item -ItemType Directory -Force -Path $release,$debug | Out-Null

    # Override the project's hardcoded Debug output C:\Tao moi\ to protect installed DLL.
    $rc = Run-Tool "BUILD_RELEASE_X64" $msbuild @(
        $project, "/t:Rebuild", "/p:Configuration=Release", "/p:Platform=x64",
        "/p:OutputPath=$release\", "/v:minimal"
    ) $logRoot
    if ($rc -ne 0) {
        $status.BUILD_RELEASE_X64 = "FAIL"
        throw "Release build failure"
    }
    $candidate = Require-File (Join-Path $release "ADDIN.dll") "Release ADDIN.dll"
    $status.BUILD_RELEASE_X64 = "PASS"
    $status.CANDIDATE_DLL_SHA256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $candidate).Hash

    $rc = Run-Tool "BUILD_DEBUG_X64" $msbuild @(
        $project, "/t:Rebuild", "/p:Configuration=Debug", "/p:Platform=x64",
        "/p:OutputPath=$debug\", "/v:minimal"
    ) $logRoot
    if ($rc -ne 0) { $status.BUILD_DEBUG_X64 = "FAIL"; throw "Debug build failure" }
    [void](Require-File (Join-Path $debug "ADDIN.dll") "Debug ADDIN.dll")
    $status.BUILD_DEBUG_X64 = "PASS"

    $testExe = Join-Path $logRoot "Cg2TestHarness.exe"
    $testSource = Require-File (Join-Path $src "CG2_TEST_HARNESS.cs") "Test C# source"
    $rc = Run-Tool "COMPILE_HARNESS" $csc @(
        "/nologo", "/target:exe", "/langversion:7.3", "/platform:x64",
        "/r:System.Core.dll", "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll",
        "/out:$testExe", $testSource
    ) $logRoot
    if ($rc -ne 0) { $status.OFFLINE_PATCH_TEST = "FAIL"; throw "Harness compiler failed" }
    $rc = Run-Tool "RUN_HARNESS" $testExe @($candidate) $logRoot
    $status.OFFLINE_PATCH_TEST = if ($rc -eq 0) { "PASS" } else { "FAIL" }
    if ($rc -ne 0) { throw "Offline tests failed: no CAD executed" }

    $runnerExe = Join-Path $logRoot "Cg2LiveCadRunner.exe"
    $runner = Require-File (Join-Path $src "CG2_LIVE_CAD_RUNNER.cs") "Runner C# source"
    $repeat = Require-File (Join-Path $src "CG2_REVIEW3_REPEATABILITY.cs") "Repeatability C# source"
    $siteAssert = Require-File (Join-Path $src "CG2_1_SITE_ASSERT.cs") "Site assertion C# source"
    $rc = Run-Tool "COMPILE_RUNNER" $csc @(
        "/nologo", "/target:exe", "/langversion:7.3", "/platform:x64",
        "/r:System.Core.dll", "/r:$sw", "/r:$swconst", "/out:$runnerExe",
        $runner, $repeat, $siteAssert
    ) $logRoot
    if ($rc -ne 0) { $status.RUNNER_SELF_TEST = "FAIL"; throw "Runner compiler failed" }
    $rc = Run-Tool "RUNNER_SELF_TEST" $runnerExe @("--self-test") $logRoot
    $status.RUNNER_SELF_TEST = if ($rc -eq 0) { "PASS" } else { "FAIL" }
    if ($rc -ne 0) { throw "Runner self-test failed" }

    if ($RunCad) {
        if (Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue) {
            throw "Existing SOLIDWORKS session detected; CAD tests refused"
        }
        $cadArgs = @($BaselineDll, $candidate, $SandboxRoot) + $models
        $rc = Run-Tool "CAD_REGRESSION" $runnerExe $cadArgs $logRoot
        $status.CAD_REGRESSION = if ($rc -eq 0) { "PASS" } else { "FAIL" }
        $status.RAW_CSV_BYTE_PARITY = if ($rc -eq 0) {
            "PASS"
        } else {
            "FAIL_OR_NOT_EVALUATED_SEE_LOG"
        }
        if ($rc -ne 0) { $failed = $true }
        if ($RepeatabilityFixture) {
            if (Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue) {
                throw "Active SOLIDWORKS after regression: repeatability test refused"
            }
            $rc = Run-Tool "CAD_REPEATABILITY" $runnerExe @(
                $BaselineDll, $candidate, $SandboxRoot,
                $RepeatabilityFixture, "--repeatability"
            ) $logRoot
            $status.CAD_REPEATABILITY = if ($rc -eq 0) { "PASS" } else { "FAIL" }
            if ($rc -ne 0) { $failed = $true }
        }
    }
} catch {
    $failed = $true
    $status.ERROR = $_.Exception.Message
    Write-Host ("STOP_OR_LIMITATION=" + $_.Exception.Message) -ForegroundColor Red
} finally {
    $summary = Join-Path $logRoot "CG2_1_ONE_SHOT_SUMMARY.txt"
    $status.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" } |
        Set-Content -LiteralPath $summary -Encoding UTF8
    Get-Content -LiteralPath $summary | ForEach-Object { Write-Host $_ }
    Write-Host "LOCAL_EVIDENCE_FOLDER=$logRoot"
    Write-Host "Private CAD logs/CSV stay local. Do not push raw evidence without authorization."
    Write-Host "Exact nested Part/offset sites require fixture-specific assertions; do not claim PASS based on aggregate counters."
}
if ($failed) { exit 1 }
exit 0
