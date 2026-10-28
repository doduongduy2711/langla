param([switch]$ChooseFolder,[switch]$Stop,[switch]$CheckOnly,[switch]$NoBrowser,[string]$SamplesDirectory='')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PSScriptRoot)
$python=Join-Path $root 'runtime/python.exe'
$scriptFile=Join-Path $root 'CaptchaOcr.py'
$data=Join-Path $root 'Data'
$stateFile=Join-Path $data 'portal-state.json'
if(!(Test-Path -LiteralPath $python -PathType Leaf) -or !(Test-Path -LiteralPath $scriptFile -PathType Leaf)){throw 'Giai nen day du thu muc OCR-Portal truoc khi mo.'}
if($CheckOnly){
 $info=New-Object Diagnostics.ProcessStartInfo;$info.FileName=$python;$info.Arguments='-I "'+$scriptFile+'" --worker'
 $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.RedirectStandardInput=$true;$info.EnvironmentVariables['OMP_NUM_THREADS']='1'
 $process=[Diagnostics.Process]::Start($info)
 try{
  $process.StandardInput.Close();$output=$process.StandardOutput.ReadToEndAsync();$errors=$process.StandardError.ReadToEndAsync();$watch=[Diagnostics.Stopwatch]::StartNew()
  while(!$process.WaitForExit(500)){if($watch.Elapsed.TotalSeconds -gt 60){$process.Kill();throw 'OCR runtime check timed out'}}
  if($process.ExitCode -ne 0 -or $output.Result -notmatch 'READY'){throw ('OCR runtime unavailable: '+$errors.Result)}
  Write-Output 'PASS: standalone bundled Python and OCR engine initialize without QLTK or installed Python.'
 }finally{$process.Dispose()}
 return
}
function Read-PortalHealth([int]$port){try{return Invoke-RestMethod -Uri ('http://127.0.0.1:'+$port+'/health') -TimeoutSec 1 -ErrorAction Stop}catch{return $null}}
if($Stop){
 if(!(Test-Path -LiteralPath $stateFile)){Write-Output 'Khong co portal duoc mo tu thu muc nay.';return}
 $state=Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8 | ConvertFrom-Json
 $process=Get-Process -Id ([int]$state.pid) -ErrorAction SilentlyContinue
 if($process -and $process.StartTime.ToUniversalTime().Ticks.ToString() -eq [string]$state.started -and [string]::Equals($process.MainModule.FileName,$python,[StringComparison]::OrdinalIgnoreCase)){
  Stop-Process -Id $process.Id -ErrorAction Stop
  Write-Output 'Da dung portal cua thu muc nay.'
 }else{Write-Output 'Portal nay da dung; khong tac dong tien trinh khac.'}
 return
}
if(!$SamplesDirectory){$SamplesDirectory=Join-Path $data 'samples'}
if($ChooseFolder){
 Add-Type -AssemblyName System.Windows.Forms
 $dialog=New-Object Windows.Forms.FolderBrowserDialog;$dialog.Description='Chon thu muc anh OCR (vi du Data\OCR\samples cua QLTK)'
 try{if($dialog.ShowDialog() -ne [Windows.Forms.DialogResult]::OK){return};$SamplesDirectory=$dialog.SelectedPath}finally{$dialog.Dispose()}
}
$samples=[IO.Path]::GetFullPath($SamplesDirectory)
$null=[IO.Directory]::CreateDirectory($samples);$null=[IO.Directory]::CreateDirectory($data)
$portNumber=0
foreach($candidate in 47662..47671){
 $health=Read-PortalHealth $candidate
 if($health -and $health.app -eq 'qltk-ocr-portal' -and [string]::Equals($health.samples,$samples,[StringComparison]::OrdinalIgnoreCase)){
  $url='http://127.0.0.1:'+$candidate
  if(!$NoBrowser){Start-Process $url};Write-Output ('Portal da chay: '+$url);return
 }
 $listener=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,$candidate)
 try{$listener.Start();$portNumber=$candidate;break}catch{}finally{$listener.Stop()}
}
if(!$portNumber){throw 'Cac cong 47662-47671 dang duoc su dung. Hay dong mot portal cu roi thu lai.'}
$portalArguments='-I "'+$scriptFile+'" --portal --port '+$portNumber+' --samples "'+$samples+'"'
$env:OMP_NUM_THREADS='1'
$process=Start-Process -FilePath $python -ArgumentList $portalArguments -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $data 'portal-output.log') -RedirectStandardError (Join-Path $data 'portal-errors.log') -PassThru
$state=@{pid=$process.Id;started=$process.StartTime.ToUniversalTime().Ticks.ToString();url=('http://127.0.0.1:'+$portNumber);samples=$samples}
[IO.File]::WriteAllText($stateFile,($state | ConvertTo-Json -Compress),(New-Object Text.UTF8Encoding($false)))
$watch=[Diagnostics.Stopwatch]::StartNew()
while($watch.Elapsed.TotalSeconds -lt 30){
 $health=Read-PortalHealth $portNumber
 if($health -and $health.app -eq 'qltk-ocr-portal' -and [string]::Equals($health.samples,$samples,[StringComparison]::OrdinalIgnoreCase)){
  if(!$NoBrowser){Start-Process $state.url};Write-Output ('Portal da mo: '+$state.url);return
 }
 if($process.HasExited){throw 'Portal chua mo duoc. Hay chay MO-PORTAL.cmd lai de thu lai.'}
 Start-Sleep -Milliseconds 300
}
if(!$process.HasExited){$process.Kill()}
throw 'Portal khoi dong qua lau. Hay giai nen lai goi day du va thu lai.'
