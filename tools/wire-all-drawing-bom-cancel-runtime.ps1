$ErrorActionPreference = 'Stop'

$root = 'C:\SGN26\addin\ADDIN'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Read-Normalized([string]$path) {
    return [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
}

function Write-Normalized([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text.Replace("`n", "`r`n"), $utf8)
}

function Replace-Exact([string]$source, [string]$old, [string]$new, [string]$label) {
    if (-not $source.Contains($old)) {
        if ($source.Contains($new)) { return $source }
        throw "Khong tim thay context: $label"
    }
    return $source.Replace($old, $new)
}

$actionsPath = Join-Path $root 'Commands\ThaoTacBomTaskPane.cs'
$actions = Read-Normalized $actionsPath
$actions = Replace-Exact $actions '        public void LoadBom()' '        public void LoadBom(Func<bool> isCancellationRequested = null)' 'LoadBom signature'
$actions = Replace-Exact $actions @'
            bomLoader.LoadBOMTableToGrid(gridBom, swTable);
            AutoFitBomGrid();
'@ @'
            bomLoader.LoadBOMTableToGrid(gridBom, swTable, isCancellationRequested);
            if (isCancellationRequested != null && isCancellationRequested())
            {
                lblStatus.Text = "Da huy CAP NHAT BOM.";
                return;
            }
            AutoFitBomGrid();
'@ 'LoadBom callback'
Write-Normalized $actionsPath $actions

$xepPath = Join-Path $root 'Commands\XepUnitDrawing.cs'
$xep = Read-Normalized $xepPath
$xep = Replace-Exact $xep @'
            Action<int> beginProgress,
            Action<int, int> updateProgress,
            Action finishProgress)
        {
            ModelDoc2 activeModel = swApp?.ActiveDoc as ModelDoc2;
'@ @'
            Action<int> beginProgress,
            Action<int, int> updateProgress,
            Action finishProgress,
            Func<bool> isCancellationRequested = null)
        {
            ModelDoc2 activeModel = swApp?.ActiveDoc as ModelDoc2;
'@ 'XEP UNIT signature'
$xep = Replace-Exact $xep @'
            if (drawingPaths.Count == 0)
            {
'@ @'
            if (IsCancellationRequested(isCancellationRequested))
                return;

            if (drawingPaths.Count == 0)
            {
'@ 'XEP UNIT cancel after collect'
$xep = Replace-Exact $xep @'
                    updateProgress?.Invoke(currentCount, totalCount);
                    Application.DoEvents();

                    Debug.WriteLine("[XEP UNIT] Drawing=" + drawingPath);
'@ @'
                    updateProgress?.Invoke(currentCount, totalCount);
                    Application.DoEvents();
                    if (IsCancellationRequested(isCancellationRequested))
                        break;

                    Debug.WriteLine("[XEP UNIT] Drawing=" + drawingPath);
'@ 'XEP UNIT loop cancel'
$xep = Replace-Exact $xep @'
            MessageBox.Show(
                "XEP UNIT xong."
'@ @'
            if (IsCancellationRequested(isCancellationRequested))
                return;

            MessageBox.Show(
                "XEP UNIT xong."
'@ 'XEP UNIT canceled result'

$xep = Replace-Exact $xep @'
        public void OpenCheckedAssemblyDrawings(
            DataGridView gridBom,
            Action<int> beginProgress,
            Action<int, int> updateProgress,
            Action finishProgress)
'@ @'
        public void OpenCheckedAssemblyDrawings(
            DataGridView gridBom,
            Action<int> beginProgress,
            Action<int, int> updateProgress,
            Action finishProgress,
            Func<bool> isCancellationRequested = null)
'@ 'OPEN ASSEM signature'
$xep = Replace-Exact $xep @'
                    updateProgress?.Invoke(i + 1, drawingPaths.Count);
                    Application.DoEvents();

                    if (string.IsNullOrWhiteSpace(drawingPath) || !File.Exists(drawingPath))
'@ @'
                    updateProgress?.Invoke(i + 1, drawingPaths.Count);
                    Application.DoEvents();
                    if (IsCancellationRequested(isCancellationRequested))
                        break;

                    if (string.IsNullOrWhiteSpace(drawingPath) || !File.Exists(drawingPath))
'@ 'OPEN ASSEM loop cancel'
$xep = Replace-Exact $xep @'
                ActivateDrawing(lastDrawing);
            }
            finally
'@ @'
                if (!IsCancellationRequested(isCancellationRequested))
                    ActivateDrawing(lastDrawing);
            }
            finally
'@ 'OPEN ASSEM final activation'
$xep = Replace-Exact $xep @'
            MessageBox.Show(
                "OPEN ASSEM xong."
'@ @'
            if (IsCancellationRequested(isCancellationRequested))
                return;

            MessageBox.Show(
                "OPEN ASSEM xong."
'@ 'OPEN ASSEM canceled result'
$xep = Replace-Exact $xep @'
        public List<string> GetCheckedAssemblyDrawingPaths(DataGridView gridBom)
'@ @'
        private static bool IsCancellationRequested(Func<bool> callback)
        {
            try
            {
                return callback != null && callback();
            }
            catch
            {
                return false;
            }
        }

        public List<string> GetCheckedAssemblyDrawingPaths(DataGridView gridBom)
'@ 'cancel helper'
Write-Normalized $xepPath $xep

$controlPath = Join-Path $root 'BomTaskPaneControl.cs'
$control = Read-Normalized $controlPath
$control = Replace-Exact $control '                actions?.LoadBom();' '                actions?.LoadBom(IsDrawingBomCancelRequested);' 'CAP NHAT handler callback'
$control = Replace-Exact $control @'
                xepUnitDrawing?.Run(dgvModelBom, BeginProgress, UpdateProgress, FinishProgress));
'@ @'
                xepUnitDrawing?.Run(dgvModelBom, BeginProgress, UpdateProgress, FinishProgress,
                    IsDrawingBomCancelRequested));
'@ 'XEP UNIT handler callback'
$control = Replace-Exact $control @'
                    UpdateProgress,
                    FinishProgress));
'@ @'
                    UpdateProgress,
                    FinishProgress,
                    IsDrawingBomCancelRequested));
'@ 'OPEN ASSEM handler callback'
Write-Normalized $controlPath $control

Write-Output 'CANCEL wired to all Drawing BOM commands in runtime source.'
