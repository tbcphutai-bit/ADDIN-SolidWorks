# Auto Frame Joint / Check Hole: guarded CG2 -> original Debug F8 recovery.
# Mode Inspect is read-only. Recover requires two affirmative flags.
[CmdletBinding()]
param(
 [ValidateSet('Inspect','Recover')][string]$Mode='Inspect',
 [switch]$Approved,
 [switch]$ConfirmReplaceInstalledCG2,
 [string]$InstalledDir='C:\Tao moi',
 [string]$F8Archive='C:\Tao moi\backup_addin_pre_launcher_smoke',
 [string]$CG2Archive='C:\Tao moi\backup_cg2_debug_20261019_2137',
 [string]$ManifestPath='C:\Tao moi\ADDIN-AutoFrameJoint-HostBackup\host-test-20261019-214104-728\deployment.json',
 [string]$RecoveryRoot='C:\Tao moi\ADDIN-AutoFrameJoint-HostBackup'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$Cg2DllHash='0A41103C4D04C85136777E2AD34F83ED7D311C906F7DECCF8DB7229FA19D02EC'
$Cg2PdbHash='C73F9B713AB3279E068AA8F98D7E3C979BBB72EE992A57E6CF1C51054E84A128'
$F8DllHash='F8D0A4AC2E77A93B2D008EE213BAEFBE6E57EDD29ECBDAA5A7AFA5C505BDD126'
$F8PdbHash='754571AACA727A404C8802E4D569A86E12DDC6EA06C797E27C823F5A604B7097'
$Guid='{D1C7F4B2-9A1E-4B5C-B234-A1B2C3D4E5F6}'

function Digest([string]$p) {
 if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { throw "Missing file: $p" }
 return (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToUpperInvariant()
}
function EqualLocation([string]$a,[string]$b) {
 return [string]::Equals([IO.Path]::GetFullPath($a).TrimEnd('\'),
   [IO.Path]::GetFullPath($b).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)
}
function VerifyPair([string]$dll,[string]$pdb,[string]$wantDll,[string]$wantPdb,[string]$label) {
 $d=Digest $dll
 $p=Digest $pdb
 Write-Host ("{0} DLL={1} PDB={2}" -f $label,$d,$p)
 if ($d -ne $wantDll -or $p -ne $wantPdb) { throw "$label hash mismatch. STOP." }
}
function CheckCOM([string]$installed) {
 $key=[Microsoft.Win32.Registry]::ClassesRoot.OpenSubKey("CLSID\$Guid\InprocServer32")
 if ($null -eq $key) { throw 'COM InprocServer32 key not found. STOP.' }
 $values=New-Object 'System.Collections.Generic.List[string]'
 try {
  $v=[string]$key.GetValue('CodeBase')
  if (-not [string]::IsNullOrWhiteSpace($v)) { $values.Add($v) }
  foreach($name in $key.GetSubKeyNames()) {
   $sub=$key.OpenSubKey($name)
   if ($null -eq $sub) { continue }
   try {
    $v=[string]$sub.GetValue('CodeBase')
    if (-not [string]::IsNullOrWhiteSpace($v)) { $values.Add($v) }
   } finally { $sub.Dispose() }
  }
 } finally { $key.Dispose() }
 if ($values.Count -eq 0) { throw 'COM registration has no CodeBase. STOP.' }
 foreach($v in $values) {
  $p=if ($v.StartsWith('file:',[StringComparison]::OrdinalIgnoreCase)) { ([Uri]$v).LocalPath } else { $v }
  Write-Host "COM CodeBase=$p"
  if (-not (EqualLocation $p $installed)) { throw 'Wrong COM CodeBase. STOP.' }
 }
}
function CheckNoWriters {
 foreach($name in @('SLDWORKS','devenv','MSBuild','dotnet','csc','VBCSCompiler')) {
  $p=@(Get-Process -Name $name -ErrorAction SilentlyContinue)
  if ($p.Count -gt 0) {
   throw ("Still running {0}, PID(s) {1}. Close safely before recovery." -f $name,(($p | Select-Object -ExpandProperty Id) -join ','))
  }
 }
}
$iDll=Join-Path $InstalledDir 'ADDIN.dll'
$iPdb=Join-Path $InstalledDir 'ADDIN.pdb'
$cDll=Join-Path $CG2Archive 'ADDIN.dll'
$cPdb=Join-Path $CG2Archive 'ADDIN.pdb'
$fDll=Join-Path $F8Archive 'ADDIN.dll'
$fPdb=Join-Path $F8Archive 'ADDIN.pdb'
CheckCOM $iDll
VerifyPair $cDll $cPdb $Cg2DllHash $Cg2PdbHash 'CG2 archived'
VerifyPair $fDll $fPdb $F8DllHash $F8PdbHash 'F8 archived'
$m=Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($m.originalDllHash -ne $F8DllHash -or $m.originalPdbHash -ne $F8PdbHash -or
    -not (EqualLocation ([string]$m.installedDll) $iDll) -or
    -not (EqualLocation ([string]$m.installedPdb) $iPdb)) {
 throw 'AFJ deployment manifest does not match original F8 or install target.'
}
VerifyPair ([string]$m.backupDll) ([string]$m.backupPdb) $F8DllHash $F8PdbHash 'Manifest F8'
$curDll=Digest $iDll
$curPdb=Digest $iPdb
Write-Host "CURRENT DLL=$curDll"
Write-Host "CURRENT PDB=$curPdb"
if ($Mode -eq 'Inspect') {
 foreach($name in @('SLDWORKS','devenv','MSBuild','dotnet','csc','VBCSCompiler')) {
  $p=@(Get-Process -Name $name -ErrorAction SilentlyContinue)
  if ($p.Count -gt 0) { Write-Host ("ACTIVE {0} PID={1}" -f $name,(($p | Select-Object -ExpandProperty Id) -join ',')) }
 }
 Write-Host 'READ_ONLY_INSPECT_COMPLETED'
 return
}
if (-not $Approved -or -not $ConfirmReplaceInstalledCG2) {
 throw 'Recover requires -Approved AND -ConfirmReplaceInstalledCG2.'
}
CheckNoWriters
if ($curDll -eq $F8DllHash -and $curPdb -eq $F8PdbHash) {
 Write-Host 'F8 already installed. No changes needed.'
 return
}
if ($curDll -ne $Cg2DllHash -or $curPdb -ne $Cg2PdbHash) {
 throw 'Installed pair differs from known CG2 pair. STOP.'
}
# Preserve actual current CG2 pair independently before any replacement.
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$folder=Join-Path $RecoveryRoot "recover-cg2-to-f8-$stamp"
[void](New-Item -Path $folder -ItemType Directory -ErrorAction Stop)
$bDll=Join-Path $folder 'installed.pre-recovery.cg2.dll'
$bPdb=Join-Path $folder 'installed.pre-recovery.cg2.pdb'
Copy-Item -LiteralPath $iDll -Destination $bDll -ErrorAction Stop
Copy-Item -LiteralPath $iPdb -Destination $bPdb -ErrorAction Stop
VerifyPair $bDll $bPdb $Cg2DllHash $Cg2PdbHash 'Independent CG2 backup'
$state=[ordered]@{
 Time=(Get-Date).ToString('o')
 InstalledDll=$iDll
 InstalledPdb=$iPdb
 OriginalCG2Dll=$bDll
 OriginalCG2Pdb=$bPdb
 IntendedF8DllHash=$F8DllHash
 IntendedF8PdbHash=$F8PdbHash
 Status='BACKUP_VERIFIED'
}
$stateFile=Join-Path $folder 'recovery.json'
$state | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $stateFile -Encoding UTF8
# Staging files are on the same volume, enabling File.Replace per-file atomic updates.
$stageDll=Join-Path $InstalledDir "ADDIN.$stamp.staged.dll"
$stagePdb=Join-Path $InstalledDir "ADDIN.$stamp.staged.pdb"
$oldDll=Join-Path $folder 'replace-old-cg2.dll'
$oldPdb=Join-Path $folder 'replace-old-cg2.pdb'
try {
 Copy-Item -LiteralPath $fDll -Destination $stageDll -ErrorAction Stop
 Copy-Item -LiteralPath $fPdb -Destination $stagePdb -ErrorAction Stop
 VerifyPair $stageDll $stagePdb $F8DllHash $F8PdbHash 'Staged F8'
 VerifyPair $iDll $iPdb $Cg2DllHash $Cg2PdbHash 'Last installed CG2 check'
 CheckNoWriters
 [IO.File]::Replace($stageDll,$iDll,$oldDll,$true)
 [IO.File]::Replace($stagePdb,$iPdb,$oldPdb,$true)
 VerifyPair $iDll $iPdb $F8DllHash $F8PdbHash 'Restored F8'
 $state.Status='RECOVERED_F8'
 $state | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $stateFile -Encoding UTF8
 Write-Host 'RECOVERY_PASS: restored F8 DLL and PDB.'
 Write-Host "RECOVERY_JOURNAL=$stateFile"
 Write-Host 'CG2 backup preserved. DO NOT run original AFJ Restore again.'
} catch {
 $cause=$_
 Write-Warning "Recovery failed: $($cause.Exception.Message)"
 try {
  $d=Digest $iDll; $p=Digest $iPdb
  if (($d -eq $F8DllHash -or $d -eq $Cg2DllHash) -and
      ($p -eq $F8PdbHash -or $p -eq $Cg2PdbHash)) {
   Copy-Item -LiteralPath $bDll -Destination $iDll -Force -ErrorAction Stop
   Copy-Item -LiteralPath $bPdb -Destination $iPdb -Force -ErrorAction Stop
   VerifyPair $iDll $iPdb $Cg2DllHash $Cg2PdbHash 'Rollback CG2'
   $state.Status='ROLLED_BACK_CG2'
  } else {
   $state.Status='STOP_UNEXPECTED_HASH'
   Write-Warning 'Another build appeared during recovery. No overwrite attempted.'
  }
 } catch {
  $state.Status='MANUAL_RECOVERY_REQUIRED'
  Write-Warning "Automatic rollback failed; CG2 backup remains in $folder"
 }
 $state | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $stateFile -Encoding UTF8
 throw $cause
} finally {
 foreach($p in @($stageDll,$stagePdb)) {
  if (Test-Path -LiteralPath $p -PathType Leaf) {
   Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue
  }
 }
}
