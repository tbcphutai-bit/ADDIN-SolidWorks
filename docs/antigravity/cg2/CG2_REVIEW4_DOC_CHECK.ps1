# CG2 Review #4 — documentation-only acceptance guard.
# Windows PowerShell 5.1+; no CAD access, no writes, no source modifications.
# Run from any working directory.
$ErrorActionPreference = "Stop"
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$files = @(
    (Join-Path $dir "CG2_REPORT.md"),
    (Join-Path $dir "CG2_REVIEW3_EVIDENCE.md")
)
$required = @(
    "RAW_CSV_BYTE_PARITY=FAIL",
    "BASELINE_RAW_CSV_NONDETERMINISM=OBSERVED",
    "PRE_RUN_GETCOMPONENTS_ORDER_VARIATION=OBSERVED",
    "PRODUCTION_TRAVERSAL_CAUSE=NOT_PROVEN",
    "EXACT_ALL_FIELD_GEOMETRY_PARITY=NOT_VERIFIED",
    "CANONICALIZED_ROW_PARITY=PASS_WITH_NORMALIZATION",
    "MARKER_GENERATION_PARITY=NOT_TESTED",
    "CG2_END_TO_END_ALL_SCENARIOS=NOT_VERIFIED",
    "READY_FOR_MERGE=NO",
    "CG1_REPEATABLE_BYTES=NO",
    "CG2_REPEATABLE_BYTES=NO",
    "CG1_VS_CG2_BYTE_PARITY=FAIL",
    "REPEATABILITY_EXIT_CODE=1"
)
$prohibited = @(
    "(?i)Physical/Engineering Parity:\s*100%\s*PASS",
    "(?i)Source of Non-Deterministic Byte Order:",
    "(?i)all runs are\s+\*{0,2}100%\s+identical",
    "(?i)CG2_VS_CG1_BYTE_PARITY=PASS",
    "(?i)ROOT_CAUSE=SOLIDWORKS_COM_TRAVERSAL",
    "(?i)EXACT_ALL_FIELD_GEOMETRY_PARITY=100%"
)

$errors = New-Object 'System.Collections.Generic.List[string]'
foreach ($file in $files) {
    if (-not [System.IO.File]::Exists($file)) {
        $errors.Add("MISSING_FILE: $file")
        continue
    }
    $content = [System.IO.File]::ReadAllText(
        $file, [System.Text.Encoding]::UTF8)
    $name = [System.IO.Path]::GetFileName($file)
    foreach ($status in $required) {
        $pattern = '(?m)^\s*' + [regex]::Escape($status) + '\s*$'
        $count = [regex]::Matches($content, $pattern).Count
        if ($count -ne 1) {
            $errors.Add("$name : expected exactly one '$status' status line, found $count")
        }
    }
    foreach ($pattern in $prohibited) {
        if ([regex]::IsMatch($content, $pattern)) {
            $errors.Add("$name : unsupported claim matched '$pattern'")
        }
    }
    if ($name -eq "CG2_REPORT.md" -and
        -not $content.Contains("### 5. Kết luận kỹ thuật — giới hạn bằng chứng")) {
        $errors.Add("$name : section G.5 must explicitly separate evidence and inference")
    }
    if ($name -eq "CG2_REVIEW3_EVIDENCE.md" -and
        -not $content.Contains("### Key Technical Findings — Evidence vs. Inference")) {
        $errors.Add("$name : source-of-causality section not updated")
    }
    if (-not $content.Contains("129,522")) {
        $errors.Add("$name : original CSV byte length evidence seems missing")
    }
}
if ($errors.Count -gt 0) {
    foreach ($item in $errors) {
        [Console]::Error.WriteLine("FAIL: " + $item)
    }
    [Console]::Error.WriteLine("CG2_REVIEW4_DOC_CHECK=FAIL")
    exit 1
}
Write-Output "PASS: two reports contain required exact evidence/status markers"
Write-Output "PASS: unsupported absolute-causality claims rejected"
Write-Output "PASS: raw CSV mismatch, repeatability exit=1, marker NOT_TESTED and merge NO retained"
Write-Output "CG2_REVIEW4_DOC_CHECK=PASS (DOCUMENTATION ONLY; NOT CAD PROOF)"
exit 0
