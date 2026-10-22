param(
 [Parameter(Mandatory=$true)][ValidateRange(1,999999999)][int]$Version,
 [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
 [string]$OutputDirectory=''
)
$ErrorActionPreference='Stop'
if(!$OutputDirectory){$OutputDirectory=Join-Path $PSScriptRoot '../dist'}
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$output=[IO.Path]::GetFullPath($OutputDirectory)
$null=[IO.Directory]::CreateDirectory($output)
$temp=Join-Path $output ('build-'+[guid]::NewGuid().ToString('N'));$program=Join-Path $temp 'QLTK';$null=[IO.Directory]::CreateDirectory($program)
$native=@('AutoLogin.cs','NativeBackend.cs','SocketServer.cs','StoredJson.cs','UiPerformance.cs')
$sources=@(Get-ChildItem -LiteralPath (Join-Path $root 'QLTK') -File | Where-Object {($_.Extension -eq '.ps1' -and $_.Name -notmatch '(?i)test' -and $_.Name -ne 'ProfileManager.ps1') -or $native -contains $_.Name -or $_.Name -eq 'MapCatalog.json'})
foreach($source in $sources){Copy-Item -LiteralPath $source.FullName -Destination $program}
[IO.File]::WriteAllText((Join-Path $program 'update-channel.json'),(@{repository=$Repository} | ConvertTo-Json -Compress),(New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $program 'release-version.json'),(@{version=$Version} | ConvertTo-Json -Compress),(New-Object Text.UTF8Encoding($false)))
$files=@(Get-ChildItem -LiteralPath $program -File | Sort-Object Name | ForEach-Object {[ordered]@{name=$_.Name;size=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
$manifest=@{schema=1;version=$Version;files=$files} | ConvertTo-Json -Depth 6 -Compress
$manifestPath=Join-Path $temp 'manifest.json';[IO.File]::WriteAllText($manifestPath,$manifest,(New-Object Text.UTF8Encoding($false)))
$updateZip=Join-Path $output 'QLTK-update.zip'
$stream=[IO.File]::Open($updateZip,[IO.FileMode]::CreateNew);$zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
 $null=[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$manifestPath,'manifest.json',[IO.Compression.CompressionLevel]::Optimal)
 foreach($file in $files){$null=[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $program $file.name),('QLTK/'+$file.name),[IO.Compression.CompressionLevel]::Optimal)}
}finally{$zip.Dispose();$stream.Dispose()}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$automation=[Management.Automation.PowerShell].Assembly.Location;$launcher=Join-Path $temp 'QLTK.exe'
& $compiler /nologo /target:winexe /platform:x64 "/out:$launcher" /r:System.Windows.Forms.dll "/r:$automation" (Join-Path $root 'QLTK/QltkHost.cs')
if($LASTEXITCODE -ne 0){throw 'Launcher compilation failed'}
Copy-Item -LiteralPath (Join-Path $root 'QLTK.cmd') -Destination $temp
$portableZip=Join-Path $output 'QLTK-portable.zip'
$stream=[IO.File]::Open($portableZip,[IO.FileMode]::CreateNew);$zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($name in @('QLTK.exe','QLTK.cmd')){$null=[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $temp $name),$name,[IO.Compression.CompressionLevel]::Optimal)}
 foreach($file in $files){$null=[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $program $file.name),('QLTK/'+$file.name),[IO.Compression.CompressionLevel]::Optimal)}
}finally{$zip.Dispose();$stream.Dispose()}
Write-Output ('Built v'+$Version+' with '+$files.Count+' program files; no Data, credentials, logs, user settings or game binaries.')
Get-FileHash -LiteralPath $updateZip,$portableZip -Algorithm SHA256 | Select-Object Path,Hash
