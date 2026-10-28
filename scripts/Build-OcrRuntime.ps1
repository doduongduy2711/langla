param([Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$Python="python")
$ErrorActionPreference='Stop'
function Compress-OcrRuntime([string]$runtime,[string]$zip){
 $stream=[IO.File]::Open($zip,[IO.FileMode]::CreateNew)
 $archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
 try{
  foreach($path in [IO.Directory]::EnumerateFiles($runtime,'*',[IO.SearchOption]::AllDirectories)){
   $name=$path.Substring($runtime.Length+1).Replace('\','/')
   if($name -match '(^|/)(__pycache__|tests|test)(/|$)' -or $name.EndsWith('.pyc')){continue}
   $null=[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$path,$name,[IO.Compression.CompressionLevel]::Optimal)
  }
 }finally{$archive.Dispose();$stream.Dispose()}
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$output=[IO.Path]::GetFullPath($OutputDirectory);$null=[IO.Directory]::CreateDirectory($output)
$root=Join-Path $output ('ocr-build-'+[guid]::NewGuid().ToString('N'))
$runtime=Join-Path $root 'runtime';$null=[IO.Directory]::CreateDirectory($runtime)
$archive=Join-Path $root 'python.zip'
Invoke-WebRequest -UseBasicParsing 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip' -OutFile $archive
[IO.Compression.ZipFile]::ExtractToDirectory($archive,$runtime)
@('python312.zip','.','Lib/site-packages','import site') | Set-Content -LiteralPath (Join-Path $runtime 'python312._pth') -Encoding ASCII
& $Python -m pip install --disable-pip-version-check --no-cache-dir --no-compile --only-binary=:all: --target (Join-Path $runtime 'Lib/site-packages') ddddocr==1.6.1 Pillow numpy
if($LASTEXITCODE -ne 0){throw 'OCR runtime dependency installation failed'}
'ocr-v1' | Set-Content -LiteralPath (Join-Path $runtime 'runtime-version.txt') -Encoding ASCII
$zip=Join-Path $output 'QLTK-ocr-runtime-win64.zip'
Compress-OcrRuntime $runtime $zip
Write-Output ('Built OCR runtime: '+(Get-Item -LiteralPath $zip).Length+' bytes')
