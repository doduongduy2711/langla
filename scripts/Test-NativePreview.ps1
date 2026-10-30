param([string]$OutputDirectory=(Join-Path $PSScriptRoot '../dist/native-preview-test'))
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$output=[IO.Path]::GetFullPath($OutputDirectory);$null=[IO.Directory]::CreateDirectory($output)
$reference=& (Join-Path $PSScriptRoot 'Get-Net48References.ps1')
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references=@('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Security.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')|ForEach-Object {'/r:'+(Join-Path $reference $_)}
$exe=Join-Path $output 'NativePreviewTests.exe'
& $compiler /nologo /noconfig /nostdlib+ /target:exe /platform:x64 /main:NativePreviewTests ('/out:'+$exe) $references (Join-Path $root 'QLTK/NativePreview.cs') (Join-Path $root 'QLTK/NativeFeaturePages.cs') (Join-Path $root 'QLTK/NativeBootstrap.cs') (Join-Path $root 'QLTK/NativePreviewTests.cs')
if($LASTEXITCODE -ne 0){throw 'Native preview test compilation failed'}
Copy-Item -LiteralPath (Join-Path $root 'QLTK.exe.config') -Destination ($exe+'.config')
& $exe --image (Join-Path $output 'native-preview.png')
if($LASTEXITCODE -ne 0){throw 'Native preview behavior tests failed'}
& (Join-Path $root 'QLTK/NativePreviewApiTests.ps1')

$frontend=@('NativePreview.cs','NativeFeaturePages.cs','NativeBootstrap.cs') | ForEach-Object {Join-Path $root ('QLTK/'+$_)}
$bridgeTest=Join-Path $output 'NativeUiBridgeTests.exe'
& $compiler /nologo /noconfig /nostdlib+ /target:exe /platform:x64 /main:NativeUiBridgeTests ('/out:'+$bridgeTest) $references $frontend (Join-Path $root 'QLTK/NativeUiBridge.cs') (Join-Path $root 'QLTK/NativeUiBridgeTests.cs')
if($LASTEXITCODE -ne 0){throw 'Native UI bridge tests compilation failed'}
Copy-Item -LiteralPath (Join-Path $root 'QLTK.exe.config') -Destination ($bridgeTest+'.config')
$schema=Join-Path $output 'real-pages.json'
& powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $root 'QLTK/AccountManager.ps1') -UiTest -NativeUiExport $schema
if($LASTEXITCODE -ne 0){throw 'Original manager pages export failed'}
& $bridgeTest --real-pages $schema (Join-Path $output 'native-all-functions.png')
if($LASTEXITCODE -ne 0){throw 'Native full UI bridge tests failed'}
$bundle=Join-Path $output 'isolated-bundle'
& (Join-Path $PSScriptRoot 'Build-NativePreview.ps1') -OutputDirectory $bundle
$startupTest=Join-Path $output 'NativeBackendStartupTests.exe'
& $compiler /nologo /noconfig /nostdlib+ /target:exe /platform:x64 /main:NativeBackendStartupTests ('/out:'+$startupTest) $references $frontend (Join-Path $root 'QLTK/NativeBackendStartupTests.cs')
if($LASTEXITCODE -ne 0){throw 'Hidden backend startup tests compilation failed'}
Copy-Item -LiteralPath (Join-Path $root 'QLTK.exe.config') -Destination ($startupTest+'.config')
& $startupTest $bundle
if($LASTEXITCODE -ne 0){throw 'Hidden backend startup tests failed'}
