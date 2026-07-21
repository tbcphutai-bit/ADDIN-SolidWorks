$ErrorActionPreference = 'Stop'
$path = 'C:\SGN26\addin\ADDIN\BomTaskPaneControl.cs'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")

if (-not $text.Contains("using System.Text;`n")) {
    $text = $text.Replace("using System.Runtime.InteropServices;`n", "using System.Runtime.InteropServices;`nusing System.Text;`n")
}

$oldField = @'
            private readonly Control allowedControl;

            public SolidWorksInputBlocker(Control allowed)
'@
$newField = @'
            private readonly Control allowedControl;

            [DllImport("user32.dll")]
            private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

            public SolidWorksInputBlocker(Control allowed)
'@
if ($text.Contains($oldField)) { $text = $text.Replace($oldField, $newField) }
elseif (-not $text.Contains($newField)) { throw 'Khong tim thay input blocker field.' }

$oldTarget = @'
                    control = control.Parent;
                }
                return false;
            }
'@
$newTarget = @'
                    control = control.Parent;
                }
                IntPtr root = GetAncestor(handle, 2);
                StringBuilder className = new StringBuilder(64);
                if (root != IntPtr.Zero && GetClassName(root, className, className.Capacity) > 0
                    && string.Equals(className.ToString(), "#32770", StringComparison.Ordinal))
                    return true;
                return false;
            }
'@
if ($text.Contains($oldTarget)) { $text = $text.Replace($oldTarget, $newTarget) }
elseif (-not $text.Contains($newTarget)) { throw 'Khong tim thay IsAllowedTarget.' }

[IO.File]::WriteAllText($path, $text.Replace("`n", "`r`n"), $utf8)
Write-Output 'Runtime dialogs allowed while SolidWorks workspace remains locked.'
