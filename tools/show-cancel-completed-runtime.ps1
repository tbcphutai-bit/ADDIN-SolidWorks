$ErrorActionPreference = 'Stop'

$path = 'C:\SGN26\addin\ADDIN\BomTaskPaneControl.cs'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
$old = @'
            finally
            {
                KeepDrawingBomTabVisible();
                if (outerCommand)
                    drawingBomCommandInProgress = false;
            }
'@
$new = @'
            finally
            {
                bool showCanceledMessage = outerCommand && drawingBomCancelRequested;
                KeepDrawingBomTabVisible();
                if (outerCommand)
                {
                    drawingBomCommandInProgress = false;
                    drawingBomCancelRequested = false;
                }
                if (showCanceledMessage)
                {
                    MessageBox.Show(
                        "Lenh da duoc huy va qua trinh xu ly da ket thuc.",
                        "CANCEL",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
'@
if (-not $text.Contains($old)) {
    if (-not $text.Contains($new)) { throw 'Khong tim thay RunDrawingBomCommand finally.' }
}
else {
    $text = $text.Replace($old, $new)
}
[IO.File]::WriteAllText($path, $text.Replace("`n", "`r`n"), $utf8)
Write-Output 'Cancel completion MessageBox integrated into runtime source.'
