param([string]$OutputDirectory=(Join-Path $PSScriptRoot '../dist/native-preview'))
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=[IO.Path]::GetFullPath($OutputDirectory);$null=[IO.Directory]::CreateDirectory($output)
$reference=& (Join-Path $PSScriptRoot 'Get-Net48References.ps1')
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references=@('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Security.dll') | ForEach-Object {'/r:'+(Join-Path $reference $_)}
$exe=Join-Path $output 'QLTK-CSharp-Preview.exe'
& $compiler /nologo /noconfig /nostdlib+ /target:winexe /platform:x64 /optimize+ ('/out:'+$exe) $references (Join-Path $root 'QLTK/NativePreview.cs')
if($LASTEXITCODE -ne 0){throw 'Native WinForms preview compilation failed'}
Copy-Item -LiteralPath (Join-Path $root 'QLTK.exe.config') -Destination ($exe+'.config')
Copy-Item -LiteralPath (Join-Path $root 'preview/README.txt') -Destination $output
Write-Output $exe
