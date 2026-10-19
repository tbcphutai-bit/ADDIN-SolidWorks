# Auto Frame Joint -- controlled SOLIDWORKS host smoke deployment.
# Modes: Inspect (read-only), Deploy (after approval), Restore (after test).
# Does NOT register COM, invoke regasm, start SOLIDWORKS, build or modify CAD.
[CmdletBinding()]
param(
    [ValidateSet('Inspect','Deploy','Restore')]
    [string]$Mode = 'Inspect',
    [switch]$Approved,
    [switch]$ConfirmTemporaryCG1Replacement,
    [string]$InstalledDll = 'C:\Tao moi\ADDIN.dll',
    [string]$TestDll = 'C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\bin\x64\Debug\ADDIN.dll',
    [string]$BackupRoot = 'C:\Tao moi\ADDIN-AutoFrameJoint-HostBackup',
    [string]$ManifestPath = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExpectedInstalledSha256 = 'D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E'
# Pinned to the exact currently installed Check Hole CG1 Release baseline.
# A DIFFERENT installed hash means STOP and re-review, not bypass.
$ExpectedTestSha256 = '643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B'
$SwAddinClsid = '{D1C7F4B2-9A1E-4B5C-B234-A1B2C3D4E5F6}'

function Sha([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "File not found: $Path"
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Full([string]$Path) {
    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function SamePath([string]$A,[string]$B) {
    return [string]::Equals((Full $A),(Full $B),
        [System.StringComparison]::OrdinalIgnoreCase)
}

function RegisteredAssemblyPaths {
    # Class registration may put CodeBase on version-specific InprocServer32 subkeys.
    $results = New-Object 'System.Collections.Generic.List[string]'
    $baseKey = [Microsoft.Win32.Registry]::ClassesRoot.OpenSubKey(
        "CLSID\$SwAddinClsid\InprocServer32")
    if ($null -eq $baseKey) { return @() }

    try {
        $keys = New-Object 'System.Collections.Generic.List[Microsoft.Win32.RegistryKey]'
        $keys.Add($baseKey)
        foreach ($name in $baseKey.GetSubKeyNames()) {
            $child = $baseKey.OpenSubKey($name)
            if ($null -ne $child) { $keys.Add($child) }
        }
        foreach ($key in $keys) {
            $value = [string]$key.GetValue('CodeBase')
            if ([string]::IsNullOrWhiteSpace($value)) { continue }
            try {
                if ($value.StartsWith('file:',[System.StringComparison]::OrdinalIgnoreCase)) {
                    $path = ([Uri]$value).LocalPath
                } else {
                    $path = $value
                }
                if (-not [string]::IsNullOrWhiteSpace($path)) {
                    $results.Add((Full $path))
                }
            } catch { Write-Warning "Unparseable CodeBase: $value" }
        }
        foreach ($key in $keys) {
            if (-not [object]::ReferenceEquals($key,$baseKey)) { $key.Dispose() }
        }
    } finally {
        $baseKey.Dispose()
    }
    return @($results | Sort-Object -Unique)
}

function AssertClosed {
    if (@(Get-Process -Name 'SLDWORKS' -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "SOLIDWORKS is running. Save work and exit SOLIDWORKS before changing DLLs."
    }
}

function AssertRegisteredAtInstalledLocation {
    $locations = @(RegisteredAssemblyPaths)
    if ($locations.Count -eq 0) {
        throw "Cannot confirm COM CodeBase for $SwAddinClsid. STOP; do not deploy. Ask code owner to verify actual Assembly.Location."
    }
    Write-Host "Registered CodeBase path(s):"
    foreach ($location in $locations) { Write-Host " - $location" }
    foreach ($location in $locations) {
        if (-not (SamePath $location $InstalledDll)) {
            throw "Registered CodeBase differs from intended installed DLL. STOP."
        }
    }
}

$InstalledDll = Full $InstalledDll
$TestDll = Full $TestDll
$InstalledPdb = [System.IO.Path]::ChangeExtension($InstalledDll,'pdb')
$TestPdb = [System.IO.Path]::ChangeExtension($TestDll,'pdb')

if ($Mode -eq 'Inspect') {
    Write-Host "=== READ ONLY INSPECTION ==="
    Write-Host "Installed: $InstalledDll"
    Write-Host "Test:      $TestDll"
    Write-Host "SOLIDWORKS running:" ([bool]@(Get-Process -Name 'SLDWORKS' -ErrorAction SilentlyContinue).Count)
    Write-Host "CodeBase(s):" ( @(RegisteredAssemblyPaths) -join '; ' )
    if (Test-Path -LiteralPath $InstalledDll) {
        Write-Host "Installed SHA256:" (Sha $InstalledDll)
    }
    if (Test-Path -LiteralPath $TestDll) {
        Write-Host "Test SHA256:     " (Sha $TestDll)
    }
    Write-Host "Expected installed: $ExpectedInstalledSha256"
    Write-Host "Expected test:      $ExpectedTestSha256"
    Write-Host "NO FILES MODIFIED"
    return
}

if (-not $Approved) {
    throw "Write operation requires -Approved after user's explicit host-test authorization."
}
AssertClosed

if ($Mode -eq 'Deploy') {
    if (-not $ConfirmTemporaryCG1Replacement) {
        throw "Deploy would temporarily replace the installed Check Hole CG1 Release. Explicit -ConfirmTemporaryCG1Replacement is required."
    }
    Write-Host "WARNING: Test DLL temporarily replaces current Check Hole CG1 Release."
    Write-Host "The exact CG1 DLL and PDB will be backed up and MUST be restored after host test."
    AssertRegisteredAtInstalledLocation
    if ((Sha $InstalledDll) -ne $ExpectedInstalledSha256) {
        throw "Installed DLL SHA256 changed since Antigravity audit. STOP; re-review."
    }
    if ((Sha $TestDll) -ne $ExpectedTestSha256) {
        throw "Test DLL SHA256 mismatch. STOP; use exact reviewed launcher build."
    }
    [void](Sha $InstalledPdb)
    [void](Sha $TestPdb)
    if (SamePath $InstalledDll $TestDll) { throw "Test and installed DLL are the same path." }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $folder = Join-Path $BackupRoot "host-test-$stamp"
    $null = New-Item -Path $folder -ItemType Directory -ErrorAction Stop
    $backupDll = Join-Path $folder 'ADDIN.original.dll'
    $backupPdb = Join-Path $folder 'ADDIN.original.pdb'
    Copy-Item -LiteralPath $InstalledDll -Destination $backupDll -ErrorAction Stop
    Copy-Item -LiteralPath $InstalledPdb -Destination $backupPdb -ErrorAction Stop

    if ((Sha $backupDll) -ne $ExpectedInstalledSha256 -or
        (Sha $backupPdb) -ne (Sha $InstalledPdb)) {
        throw "Backup hash mismatch. No deployment performed."
    }
    $manifest = [ordered]@{
        createdAt = (Get-Date).ToString('o')
        sourceGitCommit = '1c6f27d015bdc5814d6dd2a27d5a45871eaf2ea6'
        installedDll = $InstalledDll
        installedPdb = $InstalledPdb
        backupDll = $backupDll
        backupPdb = $backupPdb
        originalDllHash = (Sha $backupDll)
        originalPdbHash = (Sha $backupPdb)
        testDllHash = (Sha $TestDll)
        testPdbHash = (Sha $TestPdb)
    }
    $manifestFile = Join-Path $folder 'deployment.json'
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestFile -Encoding UTF8
    try {
        Copy-Item -LiteralPath $TestDll -Destination $InstalledDll -Force -ErrorAction Stop
        Copy-Item -LiteralPath $TestPdb -Destination $InstalledPdb -Force -ErrorAction Stop
        if ((Sha $InstalledDll) -ne $manifest.testDllHash -or
            (Sha $InstalledPdb) -ne $manifest.testPdbHash) {
            throw "Deployed files did not match test hashes."
        }
    } catch {
        # Best effort recovery; retain backup and report for manual inspection.
        try {
            Copy-Item -LiteralPath $backupDll -Destination $InstalledDll -Force
            Copy-Item -LiteralPath $backupPdb -Destination $InstalledPdb -Force
        } catch {
            Write-Warning "Automatic rollback failed! Backups are in $folder"
        }
        throw
    }
    Write-Host "DEPLOYED TEST DLL ONLY. No registry modified."
    Write-Host "Manifest: $manifestFile"
    Write-Host "Now START SOLIDWORKS and perform host smoke tests on COPY of Assembly."
    Write-Host "After tests, CLOSE SOLIDWORKS and run Restore with this ManifestPath."
    return
}

if ($Mode -eq 'Restore') {
    if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
        throw "Restore requires -ManifestPath from successful deployment."
    }
    $m = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if (-not (SamePath $m.installedDll $InstalledDll) -or
        -not (SamePath $m.installedPdb $InstalledPdb)) {
        throw "Manifest destination does not match requested installed paths."
    }
    if ((Sha $m.backupDll) -ne $m.originalDllHash -or
        (Sha $m.backupPdb) -ne $m.originalPdbHash) {
        throw "Backup corrupted or altered; STOP."
    }
    $currentDllHash = Sha $InstalledDll
    $currentPdbHash = Sha $InstalledPdb
    if ($currentDllHash -eq $m.originalDllHash -and
        $currentPdbHash -eq $m.originalPdbHash) {
        Write-Host "Original DLL/PDB already restored; no changes."
        return
    }
    if (($currentDllHash -ne $m.testDllHash -and
         $currentDllHash -ne $m.originalDllHash) -or
        ($currentPdbHash -ne $m.testPdbHash -and
         $currentPdbHash -ne $m.originalPdbHash)) {
        throw "Installed DLL/PDB changed unexpectedly. STOP; no overwrite."
    }
    Copy-Item -LiteralPath $m.backupDll -Destination $InstalledDll -Force
    Copy-Item -LiteralPath $m.backupPdb -Destination $InstalledPdb -Force
    if ((Sha $InstalledDll) -ne $m.originalDllHash -or
        (Sha $InstalledPdb) -ne $m.originalPdbHash) {
        throw "RESTORE HASH MISMATCH. Review immediately."
    }
    Write-Host "RESTORED original ADDIN.dll and ADDIN.pdb. No registry modified."
    Write-Host "Original SHA256:" (Sha $InstalledDll)
}
