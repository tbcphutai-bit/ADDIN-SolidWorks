$ErrorActionPreference = 'Stop'
$root = 'C:\SGN26\addin\ADDIN'
$designerPath = Join-Path $root 'BomTaskPaneControl.Designer.cs'
$controlPath = Join-Path $root 'BomTaskPaneControl.cs'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Replace-Once([string]$text, [string]$old, [string]$new, [string]$label) {
    $at = $text.IndexOf($old, [StringComparison]::Ordinal)
    if ($at -lt 0) { throw "Khong tim thay context: $label" }
    if ($text.IndexOf($old, $at + $old.Length, [StringComparison]::Ordinal) -ge 0) { throw "Context bi lap: $label" }
    $text.Substring(0, $at) + $new + $text.Substring($at + $old.Length)
}

$designer = [IO.File]::ReadAllText($designerPath).Replace("`r`n", "`n").Replace("`n", "`r`n")
if ($designer -notmatch 'this\.btnCheckBalloon = new') {
    $designer = Replace-Once $designer `
        '            this.btnOpenAssem = new System.Windows.Forms.Button();' `
        "            this.btnOpenAssem = new System.Windows.Forms.Button();`r`n            this.btnCheckBalloon = new System.Windows.Forms.Button();" `
        'initialize'
    $designer = Replace-Once $designer `
        '            this.tabDrawingBom.Controls.Add(this.btnOpenAssem);' `
        "            this.tabDrawingBom.Controls.Add(this.btnOpenAssem);`r`n            this.tabDrawingBom.Controls.Add(this.btnCheckBalloon);" `
        'add control'

    $buttonBlock = @'
            //
            // btnCheckBalloon
            //
            this.btnCheckBalloon.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(242)))), ((int)(((byte)(218)))));
            this.btnCheckBalloon.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.btnCheckBalloon.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(198)))), ((int)(((byte)(139)))), ((int)(((byte)(45)))));
            this.btnCheckBalloon.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(250)))), ((int)(((byte)(226)))), ((int)(((byte)(181)))));
            this.btnCheckBalloon.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCheckBalloon.Font = new System.Drawing.Font("Meiryo UI", 8.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnCheckBalloon.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(126)))), ((int)(((byte)(78)))), ((int)(((byte)(16)))));
            this.btnCheckBalloon.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnCheckBalloon.Location = new System.Drawing.Point(242, 116);
            this.btnCheckBalloon.Name = "btnCheckBalloon";
            this.btnCheckBalloon.Padding = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.btnCheckBalloon.Size = new System.Drawing.Size(90, 42);
            this.btnCheckBalloon.TabIndex = 15;
            this.btnCheckBalloon.Text = "CHECK\r\nBALLOON";
            this.btnCheckBalloon.UseVisualStyleBackColor = false;
            //
'@ -replace "`n", "`r`n"
    $designer = Replace-Once $designer `
        "            // `r`n            // btnCheckKegaki" `
        ($buttonBlock + '            // btnCheckKegaki') `
        'property block'
    $designer = Replace-Once $designer `
        '        private System.Windows.Forms.Button btnOpenAssem;' `
        "        private System.Windows.Forms.Button btnOpenAssem;`r`n        private System.Windows.Forms.Button btnCheckBalloon;" `
        'field'
    [IO.File]::WriteAllText($designerPath, $designer, $utf8)
}

$control = [IO.File]::ReadAllText($controlPath).Replace("`r`n", "`n").Replace("`n", "`r`n")
$control = $control.Replace("        private Button btnCheckBalloon;`r`n", '')
[IO.File]::WriteAllText($controlPath, $control, $utf8)
Write-Output 'CHECK BALLOON added to WinForms Designer.'
