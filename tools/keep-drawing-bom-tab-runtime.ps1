$ErrorActionPreference = 'Stop'

$path = 'C:\SGN26\addin\ADDIN\BomTaskPaneControl.cs'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")

function Replace-Exact([string]$source, [string]$old, [string]$new, [string]$label) {
    if (-not $source.Contains($old)) {
        if ($source.Contains($new)) { return $source }
        throw "Khong tim thay context: $label"
    }
    return $source.Replace($old, $new)
}

$text = Replace-Exact $text @'
        private bool taskPaneLayoutInProgress;
'@ @'
        private bool taskPaneLayoutInProgress;
        private bool drawingBomCommandInProgress;
        private bool drawingBomCancelRequested;
'@ 'command fields'

$text = Replace-Exact $text @'
        private void btnLoadBom_Click(object sender, EventArgs e)
        {
            actions?.LoadBom();
            UpdateBomCommandButtonState();
            RefreshHostedTaskPane();
        }
'@ @'
        private void btnLoadBom_Click(object sender, EventArgs e)
        {
            RunDrawingBomCommand(() =>
            {
                actions?.LoadBom();
                UpdateBomCommandButtonState();
                RefreshHostedTaskPane();
            });
        }
'@ 'load BOM handler'

$text = Replace-Exact $text @'
        private void btnCheckDfTk_Click(object sender, EventArgs e)
        {
            actions?.CheckDfTk();
        }

        private void btnCheckUraOmote_Click(object sender, EventArgs e)
        {
            actions?.CheckUraOmote();
        }

        private void btnCheckKegaki_Click(object sender, EventArgs e)
        {
            actions?.CheckKegaki();
        }

        private void btnXepUnit_Click(object sender, EventArgs e)
        {
            xepUnitDrawing?.Run(dgvModelBom, BeginProgress, UpdateProgress, FinishProgress);
        }
'@ @'
        private void btnCheckDfTk_Click(object sender, EventArgs e)
        {
            RunDrawingBomCommand(() => actions?.CheckDfTk());
        }

        private void btnCheckUraOmote_Click(object sender, EventArgs e)
        {
            RunDrawingBomCommand(() => actions?.CheckUraOmote());
        }

        private void btnCheckKegaki_Click(object sender, EventArgs e)
        {
            RunDrawingBomCommand(() => actions?.CheckKegaki());
        }

        private void btnXepUnit_Click(object sender, EventArgs e)
        {
            RunDrawingBomCommand(() =>
                xepUnitDrawing?.Run(dgvModelBom, BeginProgress, UpdateProgress, FinishProgress));
        }
'@ 'BOM command handlers'

$oldBalloon = @'
        private void btnCheckBalloon_Click(object sender, EventArgs e)
        {
            List<string> drawingPaths = xepUnitDrawing == null
                ? new List<string>()
                : xepUnitDrawing.GetCheckedAssemblyDrawingPaths(dgvModelBom);
            if (drawingPaths.Count == 0)
            {
                MessageBox.Show("Hay tick it nhat mot UNIT co Drawing truoc.",
                    "CHECK BALLOON", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            BalloonCheckResult result = balloonChecker == null ? null : balloonChecker.RunBatch(
                drawingPaths, BeginProgress, UpdateProgress, FinishProgress);
            if (result == null)
                return;

            lblStatus.Text = result.IsOk
                ? "CHECK BALLOON: OK - " + result.ValidCount + "/" + result.ExpectedCount
                : "CHECK BALLOON: thieu " + result.MissingCount + ", trung " + result.DuplicateCount
                    + ", sai so " + result.WrongTextCount + ", dangling " + result.DanglingCount;
            result.ExportToExcel();
        }
        private void btnOpenAssem_Click(object sender, EventArgs e)
        {
            xepUnitDrawing?.OpenCheckedAssemblyDrawings(
                dgvModelBom,
                BeginProgress,
                UpdateProgress,
                FinishProgress);
        }
'@
$newBalloon = @'
        private void btnCheckBalloon_Click(object sender, EventArgs e)
        {
            RunDrawingBomCommand(() =>
            {
                List<string> drawingPaths = xepUnitDrawing == null
                    ? new List<string>()
                    : xepUnitDrawing.GetCheckedAssemblyDrawingPaths(dgvModelBom);
                if (drawingPaths.Count == 0)
                {
                    MessageBox.Show("Hay tick it nhat mot UNIT co Drawing truoc.",
                        "CHECK BALLOON", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                BalloonCheckResult result = balloonChecker == null ? null : balloonChecker.RunBatch(
                    drawingPaths, BeginProgress, UpdateProgress, FinishProgress,
                    IsDrawingBomCancelRequested);
                if (result == null)
                    return;
                if (IsDrawingBomCancelRequested())
                {
                    lblStatus.Text = "Da huy CHECK BALLOON.";
                    return;
                }

                lblStatus.Text = result.IsOk
                    ? "CHECK BALLOON: OK - " + result.ValidCount + "/" + result.ExpectedCount
                    : "CHECK BALLOON: thieu " + result.MissingCount + ", trung " + result.DuplicateCount
                        + ", sai so " + result.WrongTextCount + ", dangling " + result.DanglingCount;
                result.ExportToExcel();
            });
        }
        private void btnOpenAssem_Click(object sender, EventArgs e)
        {
            RunDrawingBomCommand(() =>
                xepUnitDrawing?.OpenCheckedAssemblyDrawings(
                    dgvModelBom,
                    BeginProgress,
                    UpdateProgress,
                    FinishProgress));
        }
'@
$text = Replace-Exact $text $oldBalloon $newBalloon 'CHECK BALLOON handler'

$text = Replace-Exact $text @'
        private void cancel_Click(object sender, EventArgs e)
        {
            actions?.RequestCancel();
        }
'@ @'
        private void RunDrawingBomCommand(Action command)
        {
            if (command == null)
                return;

            bool outerCommand = !drawingBomCommandInProgress;
            if (outerCommand)
            {
                drawingBomCommandInProgress = true;
                drawingBomCancelRequested = false;
            }

            KeepDrawingBomTabVisible();
            try
            {
                command();
            }
            finally
            {
                KeepDrawingBomTabVisible();
                if (outerCommand)
                    drawingBomCommandInProgress = false;
            }
        }

        private void KeepDrawingBomTabVisible()
        {
            if (tabBom != null && tabDrawing != null)
                tabBom.SelectedTab = tabDrawing;
            if (tabDrawingPages != null && tabDrawingBom != null)
                tabDrawingPages.SelectedTab = tabDrawingBom;
        }

        private bool IsDrawingBomCancelRequested()
        {
            return drawingBomCancelRequested;
        }

        private void cancel_Click(object sender, EventArgs e)
        {
            if (drawingBomCommandInProgress)
            {
                drawingBomCancelRequested = true;
                lblStatus.Text = "Dang huy lenh...";
            }
            actions?.RequestCancel();
        }
'@ 'cancel and tab lock'

$text = Replace-Exact $text @'
            try
            {
                SwitchTabByActiveDocument();
'@ @'
            try
            {
                if (drawingBomCommandInProgress)
                {
                    KeepDrawingBomTabVisible();
                    return 0;
                }
                SwitchTabByActiveDocument();
'@ 'active document event'

$text = Replace-Exact $text @'
        private void SwitchTabByActiveDocument()
        {
            ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;
'@ @'
        private void SwitchTabByActiveDocument()
        {
            if (drawingBomCommandInProgress)
            {
                KeepDrawingBomTabVisible();
                return;
            }

            ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;
'@ 'tab switch guard'

[IO.File]::WriteAllText($path, $text.Replace("`n", "`r`n"), $utf8)
Write-Output 'Drawing BOM tab lock integrated into runtime source.'
