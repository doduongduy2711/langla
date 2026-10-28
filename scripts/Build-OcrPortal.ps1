param([Parameter(Mandatory=$true)][string]$RuntimeArchive,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=[IO.Path]::GetFullPath($OutputDirectory);$null=[IO.Directory]::CreateDirectory($output)
$stage=Join-Path ([IO.Path]::GetTempPath()) ('OCRP-'+[guid]::NewGuid().ToString('N').Substring(0,8))
$portal=Join-Path $stage 'OCR-Portal';$runtime=Join-Path $portal 'runtime';$null=[IO.Directory]::CreateDirectory($runtime)
$archive=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($RuntimeArchive))
try{
 $total=0L
 if($archive.Entries.Count -gt 25000){throw 'Runtime archive has too many entries'}
 foreach($entry in $archive.Entries){
  $target=[IO.Path]::GetFullPath((Join-Path $runtime $entry.FullName))
  if(!$target.StartsWith($runtime+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Runtime archive path is invalid'}
  $total+=$entry.Length;if($total -gt 1073741824){throw 'Runtime archive too large'}
 }
}finally{$archive.Dispose()}
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($RuntimeArchive),$runtime)
Copy-Item -LiteralPath (Join-Path $root 'QLTK/CaptchaOcr.py') -Destination $portal
Get-ChildItem -LiteralPath (Join-Path $root 'portal') -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $portal}
& (Join-Path $portal 'Start-Portal.ps1') -CheckOnly
$zipPath=Join-Path $output 'OCR-Portal-portable.zip'
$stream=[IO.File]::Open($zipPath,[IO.FileMode]::CreateNew);$zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($path in [IO.Directory]::EnumerateFiles($portal,'*',[IO.SearchOption]::AllDirectories)){
  $relative=$path.Substring($portal.Length+1).Replace('\','/')
  if($relative.StartsWith('Data/',[StringComparison]::OrdinalIgnoreCase) -or $relative -match '(^|/)__pycache__(/|$)' -or $relative.EndsWith('.pyc')){continue}
  $null=[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$path,('OCR-Portal/'+$relative),[IO.Compression.CompressionLevel]::Optimal)
 }
}finally{$zip.Dispose();$stream.Dispose()}
Write-Output ('Standalone OCR portal: '+$zipPath)
Get-FileHash -LiteralPath $zipPath -Algorithm SHA256 | Select-Object Path,Hash
