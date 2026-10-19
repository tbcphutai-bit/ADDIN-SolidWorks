param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$MaterialLibrary,
    [Parameter(Mandatory=$true)][string]$STMaterial,
    [Parameter(Mandatory=$true)][string]$SUSMaterial,
    [Parameter(Mandatory=$true)][string]$ALMaterial
)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'New private probe directory required'}
if(-not (Test-Path -LiteralPath $MaterialLibrary -PathType Leaf)){throw 'Explicit installed material library required'}
New-Item -ItemType Directory -Path $out | Out-Null
# These are nine controlled generator tests, NOT substitutes for the user's
# missing 33 Excel cases. No company geometry or certified capacity is encoded.
$probes=@(
    @('GEN-U-ST','U',4190,4200,80,50,3.2,'ST'),
    @('GEN-U-SUS','U',4200,4200,80,50,3,'SUS'),
    @('GEN-U-AL','U',4210,4200,80,50,3,'AL'),
    @('GEN-Z-ST','Z',5990,6000,80,30,3.2,'ST'),
    @('GEN-Z-SUS','Z',6000,6000,80,30,3,'SUS'),
    @('GEN-Z-AL','Z',6010,6000,80,30,3,'AL'),
    @('GEN-THREE-ST','THREE',4200,4200,80,60,3.2,'ST'),
    @('GEN-THREE-SUS','THREE',6000,6000,80,60,3,'SUS'),
    @('GEN-THREE-AL','THREE',6010,6000,80,60,3,'AL')
)
$lines=@('CaseId,Shape,AxialLengthMm,ReferenceAxialLengthMm,W,L,ThicknessMm,MaterialGroup');$sections=@()
foreach($case in $probes){
    $lines+=(($case | ForEach-Object {[Convert]::ToString($_,[Globalization.CultureInfo]::InvariantCulture)}) -join ',')
    $t=[double]$case[6];$w=[double]$case[4];$l=[double]$case[5]
    # Explicit sketch-side convention for these test shapes. An independent
    # B-rep audit rejects a wrong outside dimension instead of adjusting CAD.
    if($case[1] -eq 'U'){$vertices=@(@(0,($l-$t)),@(0,0),@(($w-2*$t),0),@(($w-2*$t),($l-$t)))}
    elseif($case[1] -eq 'Z'){$vertices=@(@(0,0),@(40,0),@(40,($l-$t)),@($w,($l-$t)))}
    else{$vertices=@(@(0,0),@(40,0),@(40,30),@(($w-$t),30),@(($w-$t),($l-$t)))}
    $sections+=@{CaseId=$case[0];Evidence='Controlled generator geometry only; no company acceptance evidence';InsideRadiusMm=2;SketchVerticesMm=$vertices}
}
$csv=Join-Path $out 'PRIVATE-generator.csv'
$lines -join "`r`n" | Set-Content -LiteralPath $csv -Encoding UTF8 -NoNewline
$db=(Resolve-Path -LiteralPath $MaterialLibrary).Path
$spec=@{
    Version=1;Purpose='GeneratorValidation';InterpretationEvidence='Controlled native generator diagnostics, not the company 33-case matrix'
    Sources=@(@{Role='Csv';Path=$csv;Sha256=(Get-FileHash -LiteralPath $csv -Algorithm SHA256).Hash.ToLowerInvariant()})
    Columns=@{CaseId='CaseId';Shape='Shape';AxialLengthMm='AxialLengthMm';ReferenceAxialLengthMm='ReferenceAxialLengthMm';W='W';L='L';ThicknessMm='ThicknessMm';MaterialGroup='MaterialGroup'}
    Materials=@(@{Group='ST';Database=$db;Name=$STMaterial},@{Group='SUS';Database=$db;Name=$SUSMaterial},@{Group='AL';Database=$db;Name=$ALMaterial})
    Sections=$sections
}
$spec | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $out 'PRIVATE-sidecar.json') -Encoding UTF8
Write-Output (Join-Path $out 'PRIVATE-sidecar.json')
