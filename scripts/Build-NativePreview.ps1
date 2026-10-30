param([string]$OutputDirectory=(Join-Path $PSScriptRoot '../dist/native-preview'))
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=[IO.Path]::GetFullPath($OutputDirectory);$null=[IO.Directory]::CreateDirectory($output)
$reference=& (Join-Path $PSScriptRoot 'Get-Net48References.ps1')
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references=@('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Security.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') | ForEach-Object {'/r:'+(Join-Path $reference $_)}
$exe=Join-Path $output 'QLTK-CSharp-Preview.exe'
& $compiler /nologo /noconfig /nostdlib+ /target:winexe /platform:x64 /optimize+ ('/out:'+$exe) $references (Join-Path $root 'QLTK/NativePreview.cs') (Join-Path $root 'QLTK/NativeFeaturePages.cs') (Join-Path $root 'QLTK/NativeBootstrap.cs')
if($LASTEXITCODE -ne 0){throw 'Native WinForms preview compilation failed'}
Copy-Item -LiteralPath (Join-Path $root 'QLTK.exe.config') -Destination ($exe+'.config')
Copy-Item -LiteralPath (Join-Path $root 'preview/README.txt') -Destination $output
$backend=Join-Path $output 'QLTK-Backend.exe'
$hostReferences=@('mscorlib.dll','System.dll','System.Core.dll','System.Windows.Forms.dll') | ForEach-Object {'/r:'+(Join-Path $reference $_)}
$hostReferences+=('/r:'+([Management.Automation.PowerShell].Assembly.Location))
& $compiler /nologo /noconfig /nostdlib+ /target:winexe /platform:x64 /optimize+ ('/out:'+$backend) $hostReferences (Join-Path $root 'QLTK/QltkHost.cs')
if($LASTEXITCODE -ne 0){throw 'Backend host compilation failed'}
Copy-Item -LiteralPath (Join-Path $root 'QLTK.exe.config') -Destination ($backend+'.config')
$program=Join-Path $output 'QLTK';$null=[IO.Directory]::CreateDirectory($program)
$native=@('AutoLogin.cs','NativeBackend.cs','SocketServer.cs','StoredJson.cs','UiPerformance.cs','ManagerStatusCache.cs','NativeUiBridge.cs','CaptchaOcr.py')
foreach($file in Get-ChildItem -LiteralPath (Join-Path $root 'QLTK') -File | Where-Object {($_.Extension -eq '.ps1' -and $_.Name -notmatch '(?i)test' -and $_.Name -ne 'ProfileManager.ps1') -or $native -contains $_.Name -or $_.Name -in @('MapCatalog.json','release-version.json')}){Copy-Item -LiteralPath $file.FullName -Destination $program}
[IO.File]::WriteAllText((Join-Path $program 'update-channel.json'),'{"repository":"doduongduy2711/langla"}',(New-Object Text.UTF8Encoding($false)))
Write-Output $exe
