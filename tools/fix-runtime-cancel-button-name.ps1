$ErrorActionPreference = 'Stop'
$path = 'C:\SGN26\addin\ADDIN\BomTaskPaneControl.cs'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$text = [IO.File]::ReadAllText($path)
$text = $text.Replace('if (cancel != null)', 'if (button1 != null)')
$text = $text.Replace('cancel.Enabled = true;', 'button1.Enabled = true;')
$text = $text.Replace('new SolidWorksInputBlocker(cancel)', 'new SolidWorksInputBlocker(button1)')
[IO.File]::WriteAllText($path, $text, $utf8)
Write-Output 'Runtime CANCEL control linked to button1.'
