$captchaPage=New-Object Windows.Forms.TabPage;$captchaPage.Text='Giải mã ảnh';$captchaPage.BackColor=$form.BackColor;$tabs.TabPages.Add($captchaPage)
$captchaAuto=New-Object Windows.Forms.CheckBox;$captchaAuto.Text='Tự đọc, điền và xác nhận mã khi game yêu cầu';$captchaAuto.SetBounds(16,15,620,26)
$captchaAuto.Checked=if($script:settings.ContainsKey('captchaAutoEnabled')){[bool]$script:settings.captchaAutoEnabled}else{$true};$captchaPage.Controls.Add($captchaAuto)
$null=Ui-Label 'Áp dụng cho các acc đăng nhập trực tiếp bằng QLTK, kể cả tab ẩn. Đọc từng acc một; mã chưa thống nhất được lưu để kiểm tra và thử đổi ảnh.' $captchaPage 16 48 635 45
$captchaInstall=Ui-NewButton 'Cài bộ đọc mã' {Start-CaptchaInstall};Ui-Place $captchaInstall $captchaPage 16 100 175 28
$captchaRuntimeHint=Ui-Label '' $captchaPage 205 100 430 45
$captchaAccountHint=Ui-Label 'Chọn acc để xem trạng thái giải mã.' $captchaPage 16 160 635 65
$null=Ui-Label 'Máy chưa có bộ đọc: QLTK tự tải ở lần dùng đầu, hoặc bấm Cài ngay. OCR chưa đọc được: thử xác nhận mỗi 1,5 giây, tối đa 5 lần/ảnh; ảnh mới tự lưu và đọc lại.' $captchaPage 16 455 635 50
$captchaSamplesButton=Ui-NewButton 'Mở thư mục ảnh mẫu' {Open-CaptchaSamples};Ui-Place $captchaSamplesButton $captchaPage 16 515 190 28
$captchaPortalButton=Ui-NewButton 'Mở portal kiểm tra OCR' {Open-OcrPortal};Ui-Place $captchaPortalButton $captchaPage 220 515 250 28
$null=Ui-Label 'Mỗi ảnh mã mới được lưu trong Data\OCR\samples. Ảnh trùng chỉ lưu một bản; cần gán mã đúng trước khi train.' $captchaPage 16 555 635 45
$captchaManualGroup=New-Object Windows.Forms.GroupBox;$captchaManualGroup.Text='Nhập kết quả khi OCR chưa đọc được';$captchaManualGroup.SetBounds(16,230,625,220);$captchaPage.Controls.Add($captchaManualGroup)
$captchaPreview=New-Object Windows.Forms.PictureBox;$captchaPreview.SetBounds(12,25,340,100);$captchaPreview.SizeMode='Zoom';$captchaPreview.BorderStyle='FixedSingle';$captchaManualGroup.Controls.Add($captchaPreview)
$null=Ui-Label 'Mã hoặc kết quả phép tính:' $captchaManualGroup 365 18 235 20
$captchaManualCode=New-Object Windows.Forms.TextBox;$captchaManualCode.SetBounds(365,40,225,25);$captchaManualCode.MaxLength=7;$captchaManualCode.CharacterCasing='Upper';$captchaManualGroup.Controls.Add($captchaManualCode)
$captchaSaveLabel=Ui-NewButton 'Lưu kết quả' {Save-CaptchaManualResult $false};Ui-Place $captchaSaveLabel $captchaManualGroup 365 75 225 28
$captchaSaveAndSend=Ui-NewButton 'Lưu và xác nhận' {Save-CaptchaManualResult $true};Ui-Place $captchaSaveAndSend $captchaManualGroup 365 110 225 28
$captchaReload=Ui-NewButton 'Nạp ảnh đang chờ' {Update-CaptchaManualReview $true};Ui-Place $captchaReload $captchaManualGroup 365 145 225 28
$captchaChooseSample=Ui-NewButton 'Chọn ảnh đã lưu' {Select-CaptchaSample};Ui-Place $captchaChooseSample $captchaManualGroup 12 138 170 28
$captchaManualHint=Ui-Label 'Chọn acc đang chờ mã để xem ảnh.' $captchaManualGroup 12 178 595 35
$script:captchaReviewKey='';$script:captchaReviewFile='';$script:captchaReviewJobId='';$script:captchaReviewPinned=$false;$script:captchaReviewNoticeKey='';$script:captchaReviewSubmittedKey=''
$script:captchaRuntime=Join-Path $script:dataDir 'OCR\runtime';$script:captchaPython=Join-Path $script:captchaRuntime 'python.exe'
$script:captchaInstallProcess=$null;$script:captchaInstallTried=$false;$script:captchaReady=$false;$script:captchaInstallResult=Join-Path $script:dataDir 'OCR\install-result.json'
function Apply-CaptchaOptions {
 $script:captchaReady=(Test-Path -LiteralPath $script:captchaPython -PathType Leaf)
 [TGLL.NativeBackend]::ConfigureCaptcha(($captchaAuto.Checked -and !$UiTest),$script:captchaPython,(Join-Path $PSScriptRoot 'CaptchaOcr.py'),(Join-Path $script:dataDir 'OCR\jobs'))
}
function Get-CaptchaSelectedJob {
 if(!$grid.SelectedRows.Count){return $null};$a=$grid.SelectedRows[0].Tag
 if($a -and $script:accountJobs.ContainsKey([string]$a.id)){$job=Get-AccountJob $script:accountJobs[[string]$a.id];if($job -is [TGLL.NativeJob]){return $job}}
 return $null
}
function Show-CaptchaReviewSnapshot($review,[string]$jobId,[bool]$force=$false) {
 $changedAccount=$script:captchaReviewJobId -ne $jobId
 if(!$changedAccount -and !$script:captchaReviewPinned -and $review -and $review.Key -ne $script:captchaReviewKey -and $script:captchaReviewSubmittedKey -eq $script:captchaReviewKey){$captchaManualCode.Clear();$script:captchaReviewSubmittedKey=''}
 if($changedAccount -or $force){$captchaManualCode.Clear();$script:captchaReviewPinned=$false;$script:captchaReviewNoticeKey=''}
 if($changedAccount -or $force -or ($review -and !$script:captchaReviewPinned -and $review.Key -ne $script:captchaReviewKey -and !$captchaManualCode.Text)){
  if($captchaPreview.Image){$old=$captchaPreview.Image;$captchaPreview.Image=$null;$old.Dispose()}
  $script:captchaReviewKey='';$script:captchaReviewFile='';$script:captchaReviewJobId=$jobId
  if($review -and $review.SamplePath -and (Test-Path -LiteralPath $review.SamplePath -PathType Leaf)){
   try{$bytes=[IO.File]::ReadAllBytes($review.SamplePath);$stream=New-Object IO.MemoryStream(,$bytes);try{$source=[Drawing.Image]::FromStream($stream);try{$captchaPreview.Image=New-Object Drawing.Bitmap($source)}finally{$source.Dispose()}}finally{$stream.Dispose()};$script:captchaReviewKey=$review.Key;$script:captchaReviewFile=$review.SamplePath;$script:captchaReviewNoticeKey='';$captchaManualHint.Text='Nhập mã đúng rồi lưu kết quả, hoặc lưu và gửi vào game.'}catch{$captchaManualHint.Text='Chưa mở được ảnh mẫu; bấm Nạp ảnh đang chờ để thử lại.'}
  }else{$captchaManualHint.Text='Chưa có ảnh mã đã lưu của acc đang chọn.'}
 }
 $captchaSaveLabel.Enabled=[bool]$script:captchaReviewFile
 $captchaSaveAndSend.Enabled=[bool]($review -and $script:captchaReviewFile -and $review.Key -eq $script:captchaReviewKey -and $review.CanSubmit)
 if(!$script:captchaReviewPinned -and $script:captchaReviewFile -and (!$review -or $review.Key -ne $script:captchaReviewKey)){$notice=if($review){$review.Key}else{'closed'};if($notice -ne $script:captchaReviewNoticeKey){$script:captchaReviewNoticeKey=$notice;$captchaManualHint.Text='Ảnh trong game đã đổi hoặc đóng. Có thể lưu nhãn ảnh cũ; bấm Nạp ảnh đang chờ để nhập mã mới.'}}
}
function Update-CaptchaManualReview([bool]$force=$false) {
 $job=Get-CaptchaSelectedJob;$review=if($job){[TGLL.NativeBackend]::GetCaptchaReview($job)}else{$null};$jobId=if($job){[string]$job.Id}else{''}
 Show-CaptchaReviewSnapshot $review $jobId $force
}
function Select-CaptchaSample {
 if($UiTest){return}
 $directory=[IO.Path]::GetFullPath((Join-Path $script:dataDir 'OCR\samples'));$null=[IO.Directory]::CreateDirectory($directory)
 $dialog=New-Object Windows.Forms.OpenFileDialog;$dialog.Title='Chọn ảnh mẫu đã lưu';$dialog.InitialDirectory=$directory;$dialog.Filter='Ảnh mã đã lưu (*.bmp)|*.bmp';$dialog.Multiselect=$false
 try{if($dialog.ShowDialog($form) -eq [Windows.Forms.DialogResult]::OK){
  if([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($dialog.FileName)) -ine $directory){$captchaManualHint.Text='Chỉ chọn ảnh trong Data\OCR\samples.';return}
  $job=Get-CaptchaSelectedJob;$jobId=if($job){[string]$job.Id}else{''};$review=New-Object TGLL.CaptchaReview;$review.Key='saved:'+([IO.Path]::GetFileName($dialog.FileName));$review.SamplePath=$dialog.FileName
  Show-CaptchaReviewSnapshot $review $jobId $true;$script:captchaReviewPinned=$true;$captchaManualHint.Text='Ảnh đã lưu: nhập mã đúng rồi bấm Lưu kết quả. Tên ảnh được giữ nguyên.'
 }}finally{$dialog.Dispose()}
}
function Save-CaptchaManualResult([bool]$send) {
 try{
  if(!$script:captchaReviewFile){throw 'Chưa chọn được ảnh mã.'}
  $code=$captchaManualCode.Text.Trim().ToUpperInvariant()
  $null=[TGLL.NativeBackend]::SaveCaptchaLabel($script:captchaReviewFile,$code)
  $captchaManualCode.Text=$code;$captchaManualHint.Text='Đã lưu kết quả '+$code+' cùng ảnh mẫu để huấn luyện.'
  if($send){$job=Get-CaptchaSelectedJob;if($job -and [string]$job.Id -eq $script:captchaReviewJobId -and [TGLL.NativeBackend]::QueueManualCaptcha($job,$script:captchaReviewKey,$code)){$script:captchaReviewSubmittedKey=$script:captchaReviewKey;$captchaSaveAndSend.Enabled=$false;$captchaManualHint.Text='Đã lưu kết quả và xếp lệnh xác nhận cho đúng ảnh.'}else{$captchaManualHint.Text='Đã lưu kết quả. Ảnh đã đổi hoặc đã gửi trước đó; chưa gửi thêm vào game.'}}
 }catch{$captchaManualHint.Text=$_.Exception.GetBaseException().Message}
}
function Open-CaptchaSamples {
 if($UiTest){return}
 $directory=Join-Path $script:dataDir 'OCR\samples';$null=[IO.Directory]::CreateDirectory($directory)
 Start-Process -FilePath explorer.exe -ArgumentList ('"'+$directory+'"')
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
 Update-CaptchaManualReview
 if(!$grid.SelectedRows.Count){return};$a=$grid.SelectedRows[0].Tag;$j=$null
 if($script:accountJobs.ContainsKey([string]$a.id)){$j=Get-AccountJob $script:accountJobs[[string]$a.id]}
 $captchaAccountHint.Text=$a.username+' • '+$(if($j -is [TGLL.NativeJob] -and $j.CaptchaStatus){$j.CaptchaStatus}else{'Chưa gặp ảnh mã trong phiên này.'})
}
$captchaAuto.Add_CheckedChanged({Save-CaptchaPreference})
Apply-CaptchaOptions;Update-CaptchaUi

$script:ocrPortalProcess=$null;$script:ocrPortalOpening=$false
$ocrPortalTimer=New-Object Windows.Forms.Timer;$ocrPortalTimer.Interval=500
function Get-OcrPortalHealth {try{return Invoke-RestMethod -Uri 'http://127.0.0.1:47662/health' -TimeoutSec 1 -ErrorAction Stop}catch{return $null}}
function Open-OcrPortal {
 if($UiTest -or $script:ocrPortalOpening){return}
 try{
  if(!(Test-Path -LiteralPath $script:captchaPython -PathType Leaf)){Start-CaptchaInstall;$captchaRuntimeHint.Text='Cài xong bộ đọc, bấm Mở portal kiểm tra OCR.';return}
  $samples=[IO.Path]::GetFullPath((Join-Path $script:dataDir 'OCR\samples'));$health=Get-OcrPortalHealth
  if($health){if($health.app -ne 'qltk-ocr-portal' -or $health.samples -ne $samples){throw 'Cổng 47662 đang dùng bởi phiên khác.'};Start-Process 'http://127.0.0.1:47662';return}
  $info=New-Object Diagnostics.ProcessStartInfo;$info.FileName=$script:captchaPython;$info.Arguments='-I "'+(Join-Path $PSScriptRoot 'CaptchaOcr.py')+'" --portal --samples "'+$samples+'"';$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardError=$true;$info.EnvironmentVariables['OMP_NUM_THREADS']='1'
  $script:ocrPortalProcess=[Diagnostics.Process]::Start($info);$script:ocrPortalOpening=$true;$script:ocrPortalStartAt=[DateTime]::UtcNow;$captchaRuntimeHint.Text='Đang mở portal OCR trên máy này...';$ocrPortalTimer.Start()
 }catch{$captchaRuntimeHint.Text='Chưa mở được portal: '+$_.Exception.GetBaseException().Message}
}
$ocrPortalTimer.Add_Tick({
 try{
  $health=Get-OcrPortalHealth
  if($health -and $health.app -eq 'qltk-ocr-portal' -and $health.samples -eq [IO.Path]::GetFullPath((Join-Path $script:dataDir 'OCR\samples'))){$ocrPortalTimer.Stop();$script:ocrPortalOpening=$false;Start-Process 'http://127.0.0.1:47662';$captchaRuntimeHint.Text='Portal OCR đang chạy tại 127.0.0.1:47662.'}
  elseif(($script:ocrPortalProcess -and $script:ocrPortalProcess.HasExited) -or ([DateTime]::UtcNow-$script:ocrPortalStartAt).TotalSeconds -gt 20){$ocrPortalTimer.Stop();$script:ocrPortalOpening=$false;$captchaRuntimeHint.Text='Portal chưa khởi động được. Kiểm tra bộ đọc hoặc cổng 47662.'}
 }catch{$ocrPortalTimer.Stop();$script:ocrPortalOpening=$false;$captchaRuntimeHint.Text=$_.Exception.Message}
})
$form.Add_Disposed({$ocrPortalTimer.Stop();$ocrPortalTimer.Dispose();if($script:ocrPortalProcess){try{if(!$script:ocrPortalProcess.HasExited){$script:ocrPortalProcess.Kill()};$script:ocrPortalProcess.Dispose()}catch{}}})
