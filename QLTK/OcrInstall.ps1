param([Parameter(Mandatory=$true)][string]$Destination,[Parameter(Mandatory=$true)][string]$ResultFile)
$ErrorActionPreference='Stop'
function Write-Result([bool]$ok,[string]$message){
 $json=@{ok=$ok;message=$message} | ConvertTo-Json -Compress
 [IO.File]::WriteAllText($ResultFile,$json,(New-Object Text.UTF8Encoding($false)))
}
try{
 [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $target=[IO.Path]::GetFullPath($Destination);$parent=Split-Path $target -Parent
 $null=[IO.Directory]::CreateDirectory($parent)
 $stage=Join-Path $parent ('install-'+[guid]::NewGuid().ToString('N'));$null=[IO.Directory]::CreateDirectory($stage)
 $headers=@{'User-Agent'='QLTK-OCR';Accept='application/vnd.github+json'}
 $release=Invoke-RestMethod -Uri 'https://api.github.com/repos/doduongduy2711/langla/releases/tags/v262' -Headers $headers
 $assets=@($release.assets | Where-Object {$_.name -ceq 'QLTK-ocr-runtime-win64.zip'})
 if($assets.Count -ne 1 -or $release.draft -or $release.prerelease){throw 'Chưa có gói OCR chính thức.'}
 $asset=$assets[0]
 if([string]$asset.digest -notmatch '^sha256:([a-fA-F0-9]{64})$'){throw 'Gói OCR chưa có mã kiểm tra SHA-256.'}
 $expected=$Matches[1];$url=[uri]$asset.browser_download_url
 if($url.Scheme -cne 'https' -or $url.Host -cne 'github.com' -or $url.AbsolutePath -cne '/doduongduy2711/langla/releases/download/v262/QLTK-ocr-runtime-win64.zip'){throw 'Đường dẫn gói OCR không hợp lệ.'}
 $zip=Join-Path $stage 'runtime.zip';Invoke-WebRequest -UseBasicParsing -Uri $url.AbsoluteUri -OutFile $zip
 if((Get-Item -LiteralPath $zip).Length -ne $asset.size -or (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ine $expected){throw 'Gói OCR không khớp mã kiểm tra.'}
 $unpack=Join-Path $stage 'unpack';$null=[IO.Directory]::CreateDirectory($unpack)
 $prefix=[IO.Path]::GetFullPath($unpack)+[IO.Path]::DirectorySeparatorChar;$total=0L
 $archive=[IO.Compression.ZipFile]::OpenRead($zip)
 try{
  if($archive.Entries.Count -gt 15000){throw 'Gói OCR có quá nhiều tệp.'}
  foreach($entry in $archive.Entries){
   $name=$entry.FullName.Replace('/','\')
   if(!$name -or $name.Contains(':') -or $name.StartsWith('\') -or (($entry.ExternalAttributes -shr 16) -band 0xf000) -eq 0xa000){throw 'Tên tệp OCR không hợp lệ.'}
   $path=[IO.Path]::GetFullPath((Join-Path $unpack $name))
   if(!$path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Gói OCR có đường dẫn ra ngoài thư mục.'}
   $total+=$entry.Length;if($total -gt 1GB -or $entry.Length -gt 200MB){throw 'Gói OCR vượt dung lượng cho phép.'}
   if(!$entry.Name){$null=[IO.Directory]::CreateDirectory($path);continue}
   $null=[IO.Directory]::CreateDirectory((Split-Path $path -Parent))
   [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$path,$false)
  }
 }finally{$archive.Dispose()}
 if(!(Test-Path -LiteralPath (Join-Path $unpack 'python.exe')) -or !(Test-Path -LiteralPath (Join-Path $unpack 'runtime-version.txt')) -or (Get-Content -LiteralPath (Join-Path $unpack 'runtime-version.txt') -Raw).Trim() -cne 'ocr-v1'){throw 'Gói OCR thiếu bộ chạy.'}
 $info=New-Object Diagnostics.ProcessStartInfo
 $info.FileName=Join-Path $unpack 'python.exe';$info.Arguments='-I -c "import ddddocr; ddddocr.DdddOcr(show_ad=False); print(''OK'')"'
 $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 $process=New-Object Diagnostics.Process;$process.StartInfo=$info
 try{
  $null=$process.Start();$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
  if(!$process.WaitForExit(60000)){try{$process.Kill()}catch{};throw 'Bộ đọc OCR khởi động quá lâu.'}
  if($process.ExitCode -ne 0 -or !$stdout.Wait(1000) -or $stdout.Result -notmatch 'OK'){throw 'Máy chưa chạy được bộ OCR. Kiểm tra Windows 64-bit và Visual C++ Runtime.'}
 }finally{$process.Dispose()}
 if(Test-Path -LiteralPath $target){
  if(!(Test-Path -LiteralPath (Join-Path $target 'runtime-version.txt'))){throw 'Thư mục đích có dữ liệu khác; chưa thay thế.'}
  if(![IO.Path]::GetFullPath($target).StartsWith([IO.Path]::GetFullPath($parent)+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Thư mục đích không hợp lệ.'}
  $previousRuntime=Join-Path $parent ('runtime-backup-'+[guid]::NewGuid().ToString('N'))
  Move-Item -LiteralPath $target -Destination $previousRuntime
 }
 Move-Item -LiteralPath $unpack -Destination $target
 Remove-Item -LiteralPath $zip -Force
 if((Get-ChildItem -LiteralPath $stage -Force).Count -eq 0){Remove-Item -LiteralPath $stage}
 Write-Result $true 'Đã cài bộ OCR. Có thể tự đọc và xác nhận mã.'
}catch{$message=$_.Exception.Message;if($previousRuntime -and !(Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $previousRuntime)){try{Move-Item -LiteralPath $previousRuntime -Destination $target}catch{}};try{Write-Result $false $message}catch{};exit 1}
