param([string]$OutputDirectory=(Join-Path $PSScriptRoot '../dist/native-preview-test'))
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$output=[IO.Path]::GetFullPath($OutputDirectory);$null=[IO.Directory]::CreateDirectory($output)
$reference=& (Join-Path $PSScriptRoot 'Get-Net48References.ps1')
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references=@('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Security.dll')|ForEach-Object {'/r:'+(Join-Path $reference $_)}
$exe=Join-Path $output 'NativePreviewTests.exe'
& $compiler /nologo /noconfig /nostdlib+ /target:exe /platform:x64 /main:NativePreviewTests ('/out:'+$exe) $references (Join-Path $root 'QLTK/NativePreview.cs') (Join-Path $root 'QLTK/NativePreviewTests.cs')
if($LASTEXITCODE -ne 0){throw 'Native preview test compilation failed'}
Copy-Item -LiteralPath (Join-Path $root 'QLTK.exe.config') -Destination ($exe+'.config')
& $exe --image (Join-Path $output 'native-preview.png')
if($LASTEXITCODE -ne 0){throw 'Native preview behavior tests failed'}
& (Join-Path $root 'QLTK/NativePreviewApiTests.ps1')
