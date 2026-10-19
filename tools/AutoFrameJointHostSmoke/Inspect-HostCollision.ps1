# Auto Frame Joint/Check Hole DLL collision — READ ONLY inspection.
# Does not copy, delete, stop processes, register COM, build or restore.
[CmdletBinding()]
param(
    [string]$InstallDir = 'C:\Tao moi',
    [string]$DebugArchiveDir = 'C:\Tao moi\backup_addin_pre_launcher_smoke',
    [string]$Cg1ArchiveDir = 'C:\Tao moi\backup_cg1_release_snapshot',
    [string]$Cg2DebugArchiveDir = 'C:\Tao moi\backup_cg2_debug_20261019_2137',
    [string]$AfjTestDir = 'C:\Tao moi\ADDIN-AutoFrameJoint-Launcher\bin\x64\Debug',
    [string]$ManifestPath = 'C:\Tao moi\ADDIN-AutoFrameJoint-HostBackup\host-test-20261019-214104-728\deployment.json'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expect = @{
    'Original Debug F8 DLL' = 'F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126'
    'CG1 Release DLL'      = 'D9257D349ECE6E2B12006AFEA4824E2152C6BA25A80920AF2905B6F20BA08B3E'
    'CG2 Debug DLL'        = '0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC'
    'AFJ Test DLL'         = '643A093D78C64A638F0E050C4E6F9145AC54305A6F1F2A86D398A75EB8ED0D5B'
}
function Digest([string] $path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return [pscustomobject]@{ Exists=$false; Bytes=$null; SHA256=$null }
    }
    $f = Get-Item -LiteralPath $path
    $h = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
    return [pscustomobject]@{ Exists=$true; Bytes=$f.Length; SHA256=$h }
}
function ReportPair([string]$label,[string]$folder) {
    Write-Host ""
    Write-Host ("## {0}" -f $label)
    $dll = Join-Path $folder 'ADDIN.dll'
    $pdb = Join-Path $folder 'ADDIN.pdb'
    $a = Digest $dll; $b = Digest $pdb
    Write-Host ("DLL exists={0} bytes={1} SHA256={2}" -f $a.Exists,$a.Bytes,$a.SHA256)
    Write-Host ("PDB exists={0} bytes={1} SHA256={2}" -f $b.Exists,$b.Bytes,$b.SHA256)
    if (-not $a.Exists -or -not $b.Exists) { Write-Warning "Incomplete pair: $label" }
    return [pscustomobject]@{ Label=$label; Dll=$a; Pdb=$b; Folder=$folder }
}
function PairMatches($first,$second) {
    return $first.Dll.Exists -and $first.Pdb.Exists -and
      $second.Dll.Exists -and $second.Pdb.Exists -and
      $first.Dll.SHA256 -eq $second.Dll.SHA256 -and
      $first.Pdb.SHA256 -eq $second.Pdb.SHA256
}
Write-Host "=== READ-ONLY COLLISION PREFLIGHT ==="
Write-Host "Time:" (Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
Write-Host "No writes or restore calls occur in this script."

# Do not stop processes. Record if background builds are still active.
$names = @('SLDWORKS','MSBuild','dotnet','devenv','VBCSCompiler','csc','powershell','pwsh')
foreach ($name in $names) {
    $found = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    if ($found.Count -gt 0) {
        Write-Host ("RUNNING {0}: {1}" -f $name, (($found | Select-Object -ExpandProperty Id) -join ','))
    }
}

$installed = ReportPair 'Currently installed (possible CG2 overwrite)' $InstallDir
$debug = ReportPair 'Archived Debug F8' $DebugArchiveDir
$cg1 = ReportPair 'Archived CG1 Release' $Cg1ArchiveDir
$cg2 = ReportPair 'Archived CG2 Debug' $Cg2DebugArchiveDir
$afj = ReportPair 'Isolated AFJ test' $AfjTestDir

Write-Host ""
Write-Host "=== PINNED HASH CHECKS ==="
Write-Host "Archived Debug F8 matches known DLL:" ($debug.Dll.SHA256 -eq $expect['Original Debug F8 DLL'])
Write-Host "Archived CG1 matches known DLL:" ($cg1.Dll.SHA256 -eq $expect['CG1 Release DLL'])
Write-Host "Archived CG2 Debug matches known DLL:" ($cg2.Dll.SHA256 -eq $expect['CG2 Debug DLL'])
Write-Host "Isolated AFJ matches known DLL:" ($afj.Dll.SHA256 -eq $expect['AFJ Test DLL'])
Write-Host "Installed equals archived CG2 pair:" (PairMatches $installed $cg2)
Write-Host "Installed equals archived F8 pair:" (PairMatches $installed $debug)
Write-Host "Installed equals AFJ test pair:" (PairMatches $installed $afj)

Write-Host ""
Write-Host "=== DEPLOY MANIFEST READ ONLY ==="
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    Write-Warning "Manifest not found. DO NOT restore without evidence."
} else {
    $m = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    Write-Host "Manifest sourceGitCommit:" $m.sourceGitCommit
    Write-Host "Manifest originalDllHash:" $m.originalDllHash
    Write-Host "Manifest originalPdbHash:" $m.originalPdbHash
    Write-Host "Manifest testDllHash:" $m.testDllHash
    Write-Host "Manifest testPdbHash:" $m.testPdbHash
    $bd = Digest ([string]$m.backupDll)
    $bp = Digest ([string]$m.backupPdb)
    Write-Host "Manifest backup DLL hash actual:" $bd.SHA256
    Write-Host "Manifest backup PDB hash actual:" $bp.SHA256
    Write-Host "Manifest backup pair matches recorded:" (
        $bd.Exists -and $bp.Exists -and
        $bd.SHA256 -eq $m.originalDllHash -and
        $bp.SHA256 -eq $m.originalPdbHash)
    Write-Host "Manifest original DLL corresponds to archived F8:" (
        $m.originalDllHash -eq $debug.Dll.SHA256)
    Write-Host "Manifest original PDB corresponds to archived F8:" (
        $m.originalPdbHash -eq $debug.Pdb.SHA256)
}
Write-Host ""
Write-Host "END OF READ-ONLY INSPECTION."
Write-Host "DO NOT run legacy Restore while a different CG2 build is installed."
