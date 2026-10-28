$captchaPage=New-Object Windows.Forms.TabPage;$captchaPage.Text='Giải mã ảnh';$captchaPage.BackColor=$form.BackColor;$tabs.TabPages.Add($captchaPage)
$captchaAuto=New-Object Windows.Forms.CheckBox;$captchaAuto.Text='Tự đọc, điền và xác nhận mã khi game yêu cầu';$captchaAuto.SetBounds(16,15,620,26)
$captchaAuto.Checked=if($script:settings.ContainsKey('captchaAutoEnabled')){[bool]$script:settings.captchaAutoEnabled}else{$true};$captchaPage.Controls.Add($captchaAuto)
$null=Ui-Label 'Áp dụng cho các acc đăng nhập trực tiếp bằng QLTK, kể cả tab ẩn. Đọc từng acc một; mã chưa thống nhất sẽ giữ lại để kiểm tra.' $captchaPage 16 48 635 45
$captchaInstall=Ui-NewButton 'Cài bộ đọc mã' {Start-CaptchaInstall};Ui-Place $captchaInstall $captchaPage 16 100 175 28
$captchaRuntimeHint=Ui-Label '' $captchaPage 205 100 430 45
$captchaAccountHint=Ui-Label 'Chọn acc để xem trạng thái giải mã.' $captchaPage 16 160 635 65
$null=Ui-Label 'Máy chưa có bộ đọc: QLTK tự tải ở lần dùng đầu, hoặc bấm Cài ngay. Chỉ xác nhận một lần cho mỗi ảnh mã; ảnh đổi sẽ được đọc lại.' $captchaPage 16 235 635 50
$script:captchaRuntime=Join-Path $script:dataDir 'OCR\runtime';$script:captchaPython=Join-Path $script:captchaRuntime 'python.exe'
$script:captchaInstallProcess=$null;$script:captchaInstallTried=$false;$script:captchaReady=$false;$script:captchaInstallResult=Join-Path $script:dataDir 'OCR\install-result.json'
function Apply-CaptchaOptions {
 $script:captchaReady=(Test-Path -LiteralPath $script:captchaPython -PathType Leaf)
 [TGLL.NativeBackend]::ConfigureCaptcha(($captchaAuto.Checked -and !$UiTest),$script:captchaPython,(Join-Path $PSScriptRoot 'CaptchaOcr.py'),(Join-Path $script:dataDir 'OCR\jobs'))
}
function Save-CaptchaPreference {
 $script:settings.captchaAutoEnabled=$captchaAuto.Checked
 if(!$UiTest){Write-JsonAtomic $script:configPath $script:settings}
 Apply-CaptchaOptions;Update-CaptchaUi
}
function Start-CaptchaInstall {
 if($UiTest -or $script:captchaReady -or $script:captchaInstallProcess){return}
 try{
  $script:captchaInstallTried=$true;$parent=Split-Path $script:captchaRuntime -Parent;$null=[IO.Directory]::CreateDirectory($parent)
  if(Test-Path -LiteralPath $script:captchaInstallResult){Remove-Item -LiteralPath $script:captchaInstallResult -Force}
  $info=New-Object Diagnostics.ProcessStartInfo
  $info.FileName=Join-Path $PSHOME 'powershell.exe'
  $info.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $PSScriptRoot 'OcrInstall.ps1')+'" -Destination "'+$script:captchaRuntime+'" -ResultFile "'+$script:captchaInstallResult+'"'
  $info.UseShellExecute=$false;$info.CreateNoWindow=$true
  $process=New-Object Diagnostics.Process;$process.StartInfo=$info;$null=$process.Start();$script:captchaInstallProcess=$process
  $captchaRuntimeHint.Text='Đang tải và cài bộ đọc, QLTK vẫn chạy.'
 }catch{$captchaRuntimeHint.Text='Chưa cài được: '+$_.Exception.Message}
}
function Sync-CaptchaRuntime {
 if($script:captchaInstallProcess -and $script:captchaInstallProcess.HasExited){
  $script:captchaInstallProcess.Dispose();$script:captchaInstallProcess=$null
  try{$result=Get-Content -LiteralPath $script:captchaInstallResult -Raw -Encoding UTF8 | ConvertFrom-Json;$captchaRuntimeHint.Text=[string]$result.message}catch{$captchaRuntimeHint.Text='Chưa cài được bộ đọc; bấm Cài để thử lại.'}
  Apply-CaptchaOptions
 }
 if(!$UiTest -and $captchaAuto.Checked -and !$script:captchaReady -and !$script:captchaInstallTried -and [TGLL.NativeBackend]::CaptchaNeedsRuntime){Start-CaptchaInstall}
}
function Update-CaptchaUi {
 $captchaInstall.Enabled=(!$script:captchaReady -and !$script:captchaInstallProcess)
 if($script:captchaReady){$captchaRuntimeHint.Text='Bộ đọc đã sẵn sàng.'}elseif(!$script:captchaInstallProcess -and !$script:captchaInstallTried){$captchaRuntimeHint.Text='Chưa có bộ đọc. Có thể cài ngay hoặc tự tải khi gặp mã.'}
 if(!$grid.SelectedRows.Count){return};$a=$grid.SelectedRows[0].Tag;$j=$null
 if($script:accountJobs.ContainsKey([string]$a.id)){$j=Get-AccountJob $script:accountJobs[[string]$a.id]}
 $captchaAccountHint.Text=$a.username+' • '+$(if($j -is [TGLL.NativeJob] -and $j.CaptchaStatus){$j.CaptchaStatus}else{'Chưa gặp ảnh mã trong phiên này.'})
}
$captchaAuto.Add_CheckedChanged({Save-CaptchaPreference})
Apply-CaptchaOptions;Update-CaptchaUi
