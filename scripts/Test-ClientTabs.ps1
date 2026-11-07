$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=Join-Path ([IO.Path]::GetTempPath()) ('qltk-client-tabs-tests-'+[guid]::NewGuid().ToString('N'))
$null=[IO.Directory]::CreateDirectory($output)
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $output 'ClientTabsTests.exe'
& $compiler /nologo /platform:x64 /target:exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Security.dll ('/out:'+$exe) (Join-Path $root 'QLTK/ClientTabs.cs') (Join-Path $root 'QLTK/ClientTabsTests.cs')
if($LASTEXITCODE -ne 0){throw 'Client tabs compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Client tabs regression failed'}
