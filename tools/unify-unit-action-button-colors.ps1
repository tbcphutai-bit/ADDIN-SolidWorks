$ErrorActionPreference = 'Stop'
$root = 'C:\SGN26\addin\ADDIN'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Replace-Once([string]$text, [string]$old, [string]$new, [string]$label) {
    $at = $text.IndexOf($old, [StringComparison]::Ordinal)
    if ($at -lt 0) { throw "Khong tim thay context: $label" }
    if ($text.IndexOf($old, $at + $old.Length, [StringComparison]::Ordinal) -ge 0) { throw "Context bi lap: $label" }
    $text.Substring(0, $at) + $new + $text.Substring($at + $old.Length)
}

$controlPath = Join-Path $root 'BomTaskPaneControl.cs'
$control = [IO.File]::ReadAllText($controlPath).Replace("`r`n", "`n").Replace("`n", "`r`n")

$openOld = @'
            StyleToolButton(
                btnOpenAssem,
                Color.FromArgb(225, 244, 232),
                Color.FromArgb(83, 157, 105),
                Color.FromArgb(207, 237, 217),
                Color.FromArgb(28, 104, 55));
'@ -replace "`n", "`r`n"
$openNew = @'
            StyleToolButton(
                btnOpenAssem,
                Color.FromArgb(220, 235, 252),
                Color.FromArgb(82, 132, 190),
                Color.FromArgb(202, 224, 249),
                Color.FromArgb(24, 74, 126));
'@ -replace "`n", "`r`n"
$checkOld = @'
            StyleToolButton(
                btnCheckBalloon,
                Color.FromArgb(255, 242, 218),
                Color.FromArgb(198, 139, 45),
                Color.FromArgb(250, 226, 181),
                Color.FromArgb(126, 78, 16));
'@ -replace "`n", "`r`n"
$checkNew = @'
            StyleToolButton(
                btnCheckBalloon,
                Color.FromArgb(220, 235, 252),
                Color.FromArgb(82, 132, 190),
                Color.FromArgb(202, 224, 249),
                Color.FromArgb(24, 74, 126));
'@ -replace "`n", "`r`n"
$control = Replace-Once $control $openOld $openNew 'runtime OPEN ASSEM color'
$control = Replace-Once $control $checkOld $checkNew 'runtime CHECK BALLOON color'
$oldOrder = @'
            Button[] topButtons =
            {
                btnCheckDfTk,
                button2,
                btnOpenAssem,
                btnCheckBalloon,
                btnCheckUraOmote,
                btnCheckKegaki
            };
'@ -replace "`n", "`r`n"
$newOrder = @'
            Button[] topButtons =
            {
                button2,
                btnOpenAssem,
                btnCheckBalloon,
                btnCheckDfTk,
                btnCheckUraOmote,
                btnCheckKegaki
            };
'@ -replace "`n", "`r`n"
$control = Replace-Once $control $oldOrder $newOrder 'runtime button order'
[IO.File]::WriteAllText($controlPath, $control, $utf8)

$designerPath = Join-Path $root 'BomTaskPaneControl.Designer.cs'
$designer = [IO.File]::ReadAllText($designerPath).Replace("`r`n", "`n").Replace("`n", "`r`n")
$designer = $designer.Replace(
    'this.btnOpenAssem.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(225)))), ((int)(((byte)(244)))), ((int)(((byte)(232)))));',
    'this.btnOpenAssem.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(235)))), ((int)(((byte)(252)))));')
$designer = $designer.Replace(
    'this.btnOpenAssem.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(83)))), ((int)(((byte)(157)))), ((int)(((byte)(105)))));',
    'this.btnOpenAssem.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(82)))), ((int)(((byte)(132)))), ((int)(((byte)(190)))));')
$designer = $designer.Replace(
    'this.btnOpenAssem.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(207)))), ((int)(((byte)(237)))), ((int)(((byte)(217)))));',
    'this.btnOpenAssem.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(202)))), ((int)(((byte)(224)))), ((int)(((byte)(249)))));')
$designer = $designer.Replace(
    'this.btnOpenAssem.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(28)))), ((int)(((byte)(104)))), ((int)(((byte)(55)))));',
    'this.btnOpenAssem.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(24)))), ((int)(((byte)(74)))), ((int)(((byte)(126)))));')
$designer = $designer.Replace(
    'this.btnCheckBalloon.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(242)))), ((int)(((byte)(218)))));',
    'this.btnCheckBalloon.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(235)))), ((int)(((byte)(252)))));')
$designer = $designer.Replace(
    'this.btnCheckBalloon.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(198)))), ((int)(((byte)(139)))), ((int)(((byte)(45)))));',
    'this.btnCheckBalloon.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(82)))), ((int)(((byte)(132)))), ((int)(((byte)(190)))));')
$designer = $designer.Replace(
    'this.btnCheckBalloon.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(250)))), ((int)(((byte)(226)))), ((int)(((byte)(181)))));',
    'this.btnCheckBalloon.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(202)))), ((int)(((byte)(224)))), ((int)(((byte)(249)))));')
$designer = $designer.Replace(
    'this.btnCheckBalloon.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(126)))), ((int)(((byte)(78)))), ((int)(((byte)(16)))));',
    'this.btnCheckBalloon.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(24)))), ((int)(((byte)(74)))), ((int)(((byte)(126)))));')
$designer = $designer.Replace('this.button2.Location = new System.Drawing.Point(144, 66);', 'this.button2.Location = new System.Drawing.Point(46, 66);')
$designer = $designer.Replace('this.btnOpenAssem.Location = new System.Drawing.Point(242, 66);', 'this.btnOpenAssem.Location = new System.Drawing.Point(144, 66);')
$designer = $designer.Replace('this.btnCheckBalloon.Location = new System.Drawing.Point(242, 116);', 'this.btnCheckBalloon.Location = new System.Drawing.Point(242, 66);')
$designer = $designer.Replace('this.btnCheckDfTk.Location = new System.Drawing.Point(46, 66);', 'this.btnCheckDfTk.Location = new System.Drawing.Point(46, 116);')
$designer = $designer.Replace('this.btnCheckUraOmote.Location = new System.Drawing.Point(46, 116);', 'this.btnCheckUraOmote.Location = new System.Drawing.Point(144, 116);')
$designer = $designer.Replace('this.btnCheckKegaki.Location = new System.Drawing.Point(144, 116);', 'this.btnCheckKegaki.Location = new System.Drawing.Point(242, 116);')
[IO.File]::WriteAllText($designerPath, $designer, $utf8)
Write-Output 'Unit action button colors unified.'
