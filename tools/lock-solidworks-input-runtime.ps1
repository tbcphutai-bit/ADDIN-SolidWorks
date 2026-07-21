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

$text = Replace-Exact $text "using System.Runtime.InteropServices;`n" "using System.Runtime.InteropServices;`nusing System.Text;`n" 'System.Text using'

$text = Replace-Exact $text @'
        private bool drawingBomCancelRequested;
'@ @'
        private bool drawingBomCancelRequested;
        private IMessageFilter solidWorksInputBlocker;
'@ 'input blocker field'

$blockerClass = @'

        private sealed class SolidWorksInputBlocker : IMessageFilter
        {
            private readonly Control allowedControl;

            [DllImport("user32.dll")]
            private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

            public SolidWorksInputBlocker(Control allowed)
            {
                allowedControl = allowed;
            }

            public bool PreFilterMessage(ref Message message)
            {
                if (!IsUserInputMessage(message.Msg))
                    return false;
                return !IsAllowedTarget(message.HWnd);
            }

            private bool IsAllowedTarget(IntPtr handle)
            {
                Control control = Control.FromHandle(handle);
                while (control != null)
                {
                    if (control == allowedControl)
                        return true;
                    control = control.Parent;
                }
                IntPtr root = GetAncestor(handle, 2);
                StringBuilder className = new StringBuilder(64);
                if (root != IntPtr.Zero && GetClassName(root, className, className.Capacity) > 0
                    && string.Equals(className.ToString(), "#32770", StringComparison.Ordinal))
                    return true;
                return false;
            }

            private static bool IsUserInputMessage(int message)
            {
                if (message >= 0x0100 && message <= 0x0109)
                    return true;
                if (message >= 0x0201 && message <= 0x020E)
                    return true;
                if (message >= 0x00A1 && message <= 0x00AD)
                    return true;
                return message == 0x007B;
            }
        }
'@
$text = Replace-Exact $text @'
        private const string MakeHoleSizeHistoryFileName = "make-hole-sizes.txt";
'@ ($blockerClass + @'
        private const string MakeHoleSizeHistoryFileName = "make-hole-sizes.txt";
'@) 'input blocker class'

$text = Replace-Exact $text @'
            solidWorksClosing = true;
            actions?.RequestCancel();
'@ @'
            solidWorksClosing = true;
            actions?.RequestCancel();
            EndSolidWorksInputLock();
'@ 'shutdown unlock'

$text = Replace-Exact $text @'
                drawingBomCommandInProgress = true;
                drawingBomCancelRequested = false;
'@ @'
                drawingBomCommandInProgress = true;
                drawingBomCancelRequested = false;
                BeginSolidWorksInputLock();
'@ 'begin command lock'

$text = Replace-Exact $text @'
                if (outerCommand)
                {
                    drawingBomCommandInProgress = false;
                    drawingBomCancelRequested = false;
'@ @'
                if (outerCommand)
                {
                    EndSolidWorksInputLock();
                    drawingBomCommandInProgress = false;
                    drawingBomCancelRequested = false;
'@ 'end command lock'

$lockMethods = @'

        private void BeginSolidWorksInputLock()
        {
            if (solidWorksInputBlocker != null)
                return;
            if (button1 != null)
                button1.Enabled = true;
            solidWorksInputBlocker = new SolidWorksInputBlocker(button1);
            Application.AddMessageFilter(solidWorksInputBlocker);
        }

        private void EndSolidWorksInputLock()
        {
            if (solidWorksInputBlocker == null)
                return;
            Application.RemoveMessageFilter(solidWorksInputBlocker);
            solidWorksInputBlocker = null;
        }
'@
$text = Replace-Exact $text @'
        private bool IsDrawingBomCancelRequested()
'@ ($lockMethods + @'
        private bool IsDrawingBomCancelRequested()
'@) 'input lock methods'

[IO.File]::WriteAllText($path, $text.Replace("`n", "`r`n"), $utf8)
Write-Output 'SolidWorks user input lock integrated into runtime source.'
