$ErrorActionPreference = 'Stop'
$path = 'C:\SGN26\addin\ADDIN\ADDIN.csproj'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$text = [IO.File]::ReadAllText($path)
$text = $text.Replace('..\..\..\Users\SGN26\Downloads\SolidWorks.Interop.sldworks.dll', '$(USERPROFILE)\Downloads\SolidWorks.Interop.sldworks.dll')
$text = $text.Replace('..\..\..\Users\SGN26\Downloads\SolidWorks.Interop.swconst.dll', '$(USERPROFILE)\Downloads\SolidWorks.Interop.swconst.dll')
$text = $text.Replace('..\..\..\Users\SGN26\Downloads\SolidWorks.Interop.swpublished.dll', '$(USERPROFILE)\Downloads\SolidWorks.Interop.swpublished.dll')
[IO.File]::WriteAllText($path, $text, $utf8)
Write-Output 'SolidWorks interop HintPaths normalized.'
