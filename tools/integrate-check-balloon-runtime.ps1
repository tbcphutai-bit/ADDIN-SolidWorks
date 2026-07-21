$ErrorActionPreference = 'Stop'

$targetRoot = 'C:\SGN26\addin\ADDIN'
$sourceRoot = 'C:\Users\SGN26\Documents\addin'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$controlPath = Join-Path $targetRoot 'BomTaskPaneControl.cs'
$projectPath = Join-Path $targetRoot 'ADDIN.csproj'
$commandPath = Join-Path $targetRoot 'Commands\CheckBalloon.cs'
$xepUnitPath = Join-Path $targetRoot 'Commands\XepUnitDrawing.cs'
$bomLoaderPath = Join-Path $targetRoot 'BomLoader.cs'

function Replace-Once([string]$text, [string]$old, [string]$new, [string]$label) {
    $first = $text.IndexOf($old, [StringComparison]::Ordinal)
    if ($first -lt 0) { throw "Khong tim thay context: $label" }
    if ($text.IndexOf($old, $first + $old.Length, [StringComparison]::Ordinal) -ge 0) {
        throw "Context bi lap: $label"
    }
    return $text.Substring(0, $first) + $new + $text.Substring($first + $old.Length)
}

$text = [IO.File]::ReadAllText($controlPath).Replace("`r`n", "`n").Replace("`n", "`r`n")
$text = $text.Replace("using System.Collections.Generic;`r`nusing System.Globalization;",
    "using System.Collections.Generic;`r`nusing System.ComponentModel;`r`nusing System.Globalization;")
$text = $text.Replace("            InitializeComponent();`r`n            EnsureCheckBalloonButton();",
    "            InitializeComponent();`r`n            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)`r`n                return;`r`n            EnsureCheckBalloonButton();")
$text = $text.Replace('            btnCheckBalloon.Enabled = unitBomLoaded;', '            btnCheckBalloon.Enabled = hasBomRows;')
$oldDialog = @'
            MessageBox.Show(result.BuildMessage(), "Ket qua CHECK BALLOON",
                MessageBoxButtons.OK, result.IsOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
'@ -replace "`n", "`r`n"
$text = $text.Replace($oldDialog, '            result.ShowSummary(this);')
$text = $text.Replace('            result.ShowSummary(this);', '            result.ExportToExcel();')
$oldSingleCheck = @'
            BalloonCheckResult result = balloonChecker == null ? null : balloonChecker.Run();
            if (result == null)
                return;
'@ -replace "`n", "`r`n"
$newBatchCheck = @'
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
'@ -replace "`n", "`r`n"
$text = $text.Replace($oldSingleCheck, $newBatchCheck)
$text = [Text.RegularExpressions.Regex]::Replace(
    $text,
    '(?s)(if \(drawingPaths\.Count == 0\)\s*\{\s*MessageBox\.Show\()"[^"]*"',
    '$1"Hay tick it nhat mot UNIT co Drawing truoc."')
if ($text -notmatch 'btnCheckBalloon') {
    $text = Replace-Once $text '        private LenhNoteTextBalloon drawingTextAnnotationCommands;' "        private LenhNoteTextBalloon drawingTextAnnotationCommands;`r`n        private CheckBalloon balloonChecker;`r`n        private Button btnCheckBalloon;" 'fields'
    $text = Replace-Once $text "            InitializeComponent();`r`n            EnsureDimHoleButton();" "            InitializeComponent();`r`n            EnsureCheckBalloonButton();`r`n            EnsureDimHoleButton();" 'constructor'
    $text = Replace-Once $text "                cboSide,`r`n                cboBalloonProperty);" "                cboSide,`r`n                cboBalloonProperty);`r`n            balloonChecker = new CheckBalloon(swApp);" 'init command'

    $ensure = @'
        private void EnsureCheckBalloonButton()
        {
            if (btnCheckBalloon != null)
                return;

            btnCheckBalloon = new Button();
            btnCheckBalloon.Name = "btnCheckBalloon";
            btnCheckBalloon.Text = "CHECK\r\nBALLOON";
            btnCheckBalloon.Size = new Size(90, 42);
            btnCheckBalloon.TabIndex = 13;
            btnCheckBalloon.UseVisualStyleBackColor = false;
            tabDrawingBom.Controls.Add(btnCheckBalloon);
        }

'@ -replace "`n", "`r`n"
    $text = Replace-Once $text '        private void WireEvents()' ($ensure + '        private void WireEvents()') 'ensure button'
    $text = Replace-Once $text '            btnOpenAssem.Click += btnOpenAssem_Click;' "            btnOpenAssem.Click += btnOpenAssem_Click;`r`n            btnCheckBalloon.Click += btnCheckBalloon_Click;" 'wire click'
    $text = Replace-Once $text '            btnOpenAssem.Enabled = unitBomLoaded;' "            btnOpenAssem.Enabled = unitBomLoaded;`r`n            btnCheckBalloon.Enabled = unitBomLoaded;" 'enable state'
    $oldTooltip = @'
            SetBomCommandToolTip(
                btnOpenAssem,
                "BOM UNIT: m\u1EDF Drawing c\u1EE7a c\u00E1c assembly \u0111ang \u0111\u01B0\u1EE3c tick.");
'@ -replace "`n", "`r`n"
    $newTooltip = @'
            SetBomCommandToolTip(
                btnOpenAssem,
                "BOM UNIT: m\u1EDF Drawing c\u1EE7a c\u00E1c assembly \u0111ang \u0111\u01B0\u1EE3c tick.");
            SetBomCommandToolTip(
                btnCheckBalloon,
                "BOM UNIT: qu\u00E9t to\u00E0n b\u1ED9 sheet/view v\u00E0 ki\u1EC3m tra Balloon theo t\u1EEBng component instance.");
'@ -replace "`n", "`r`n"
    $text = Replace-Once $text $oldTooltip $newTooltip 'tooltip'
    $text = Replace-Once $text '            if (control == button2 || control == btnOpenAssem)' '            if (control == button2 || control == btnOpenAssem || control == btnCheckBalloon)' 'disabled tooltip'
    $text = Replace-Once $text '            Control[] commandButtons = { btnCheckDfTk, button2, btnOpenAssem, btnCheckUraOmote, btnCheckKegaki };' '            Control[] commandButtons = { btnCheckDfTk, button2, btnOpenAssem, btnCheckBalloon, btnCheckUraOmote, btnCheckKegaki };' 'hover buttons'

    $handler = @'
        private void btnCheckBalloon_Click(object sender, EventArgs e)
        {
            BalloonCheckResult result = balloonChecker == null ? null : balloonChecker.Run();
            if (result == null)
                return;

            lblStatus.Text = result.IsOk
                ? "CHECK BALLOON: OK - " + result.ValidCount + "/" + result.ExpectedCount
                : "CHECK BALLOON: thieu " + result.MissingCount + ", trung " + result.DuplicateCount
                    + ", sai so " + result.WrongTextCount + ", dangling " + result.DanglingCount;
            result.ExportToExcel();
        }

'@ -replace "`n", "`r`n"
    $text = Replace-Once $text '        private void btnOpenAssem_Click(object sender, EventArgs e)' ($handler + '        private void btnOpenAssem_Click(object sender, EventArgs e)') 'handler'
    $text = Replace-Once $text "                button2,`r`n                btnOpenAssem," "                button2,`r`n                btnOpenAssem,`r`n                btnCheckBalloon," 'layout buttons'
    $text = Replace-Once $text '            if (topButtons.Length >= 5 && pageWidth >= 420)' '            if (topButtons.Length >= 5 && pageWidth >= 420)' 'layout condition'
    $text = Replace-Once $text '                topButtonWidth = Math.Max(76, (pageWidth - topButtonGap * 4) / 5);' '                topButtonWidth = Math.Max(70, (pageWidth - topButtonGap * (topButtons.Length - 1)) / topButtons.Length);' 'layout width'
    $style = @'

            StyleBomTopButton(btnCheckBalloon);
            StyleToolButton(
                btnCheckBalloon,
                Color.FromArgb(255, 242, 218),
                Color.FromArgb(198, 139, 45),
                Color.FromArgb(250, 226, 181),
                Color.FromArgb(126, 78, 16));
'@ -replace "`n", "`r`n"
    $text = Replace-Once $text "                Color.FromArgb(28, 104, 55));`r`n        }" ("                Color.FromArgb(28, 104, 55));" + $style + "        }") 'button style'
    [IO.File]::WriteAllText($controlPath, $text, $utf8)
}
[IO.File]::WriteAllText($controlPath, $text, $utf8)

$xepText = [IO.File]::ReadAllText($xepUnitPath).Replace("`r`n", "`n").Replace("`n", "`r`n")
if ($xepText -notmatch 'GetCheckedAssemblyDrawingPaths') {
    $pathMethod = @'
        public List<string> GetCheckedAssemblyDrawingPaths(DataGridView gridBom)
        {
            List<string> drawingPaths = new List<string>();
            ModelDoc2 activeModel = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
            if (activeModel == null || activeModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                return drawingPaths;

            IBomTableAnnotation selectedBomTable =
                GetSelectedBomTable(activeModel) ?? GetFirstBomTable(activeModel);
            CollectDirectAssemblyDrawingPathsFromCheckedRows(
                gridBom, selectedBomTable, activeModel, drawingPaths);
            return drawingPaths;
        }

'@ -replace "`n", "`r`n"
    $xepText = Replace-Once $xepText '        private IBomTableAnnotation GetSelectedBomTable(ModelDoc2 drawingModel)' ($pathMethod + '        private IBomTableAnnotation GetSelectedBomTable(ModelDoc2 drawingModel)') 'checked drawing paths API'
    [IO.File]::WriteAllText($xepUnitPath, $xepText, $utf8)
}

$project = [IO.File]::ReadAllText($projectPath).Replace("`r`n", "`n").Replace("`n", "`r`n")
$project = $project.Replace('<Compile Include=\"Commands\CheckBalloon.cs\" />', '<Compile Include="Commands\CheckBalloon.cs" />')
$project = [Text.RegularExpressions.Regex]::Replace(
    $project,
    '(?m)^    <Compile Include=\\\s*$',
    "    <Compile Include=`"Commands\CheckKegaki.cs`" />`r`n    <Compile Include=`"Commands\CheckBalloon.cs`" />")
if ($project -notmatch 'Commands\\CheckBalloon.cs') {
    $project = Replace-Once $project '    <Compile Include="Commands\CheckKegaki.cs" />' "    <Compile Include=`"Commands\CheckKegaki.cs`" />`r`n    <Compile Include=`"Commands\CheckBalloon.cs`" />" 'project compile'
}
try {
    [IO.File]::WriteAllText($projectPath, $project, $utf8)
}
catch [IO.IOException] {
    $projectPath = Join-Path $targetRoot 'ADDIN.CheckBalloon.csproj'
    [IO.File]::WriteAllText($projectPath, $project, $utf8)
    Write-Output "Project goc dang bi khoa; da tao: $projectPath"
}

Copy-Item -LiteralPath (Join-Path $sourceRoot 'Commands\CheckBalloon.cs') -Destination $commandPath -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot 'BomLoader.cs') -Destination $bomLoaderPath -Force
Write-Output 'CHECK BALLOON integrated into runtime source.'
