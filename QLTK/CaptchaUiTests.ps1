$ocrPref=$captchaAuto.Checked;$ocrSetting=$script:settings.captchaAutoEnabled
try{
 if($captchaPage.Text -ne 'Giải mã ảnh' -or !$captchaPage.AutoScroll -or $tabs.TabPages.Count -ne 25){throw 'Captcha tab missing'}
 $captchaAuto.Checked=$false;Save-CaptchaPreference
 if($script:settings.captchaAutoEnabled){throw 'Captcha disable preference did not persist'}
 $captchaAuto.Checked=$true;Save-CaptchaPreference
 if(!$script:settings.captchaAutoEnabled){throw 'Captcha enable preference did not persist'}
 if($captchaSamplesButton.Text -ne 'Mở thư mục ảnh mẫu'){throw 'Captcha samples button missing'};Open-CaptchaSamples;Start-CaptchaInstall;Sync-CaptchaRuntime
 if($script:captchaInstallProcess){throw 'UI fixture unexpectedly installed OCR runtime'}
 if(!(Test-UpdateFileName 'CaptchaOcr.py') -or (Test-UpdateFileName 'unknown.py')){throw 'OCR updater filename allowlist failed'}
 $directory=Join-Path $script:dataDir 'OCR\samples';$null=[IO.Directory]::CreateDirectory($directory);$sample=Join-Path $directory ('fixture-'+[guid]::NewGuid().ToString('N')+'.bmp');$label=[IO.Path]::ChangeExtension($sample,'.label.txt')
 try{
  $bitmap=New-Object Drawing.Bitmap(100,40);try{$bitmap.Save($sample,[Drawing.Imaging.ImageFormat]::Bmp)}finally{$bitmap.Dispose()}
  $review=New-Object TGLL.CaptchaReview;$review.Key='fixture-1';$review.SamplePath=$sample;$review.CanSubmit=$true
  Show-CaptchaReviewSnapshot $review 'fixture' $true
  if(!$captchaPreview.Image -or !$captchaSaveLabel.Enabled -or !$captchaSaveAndSend.Enabled){throw 'Manual preview or buttons unavailable'}
  $captchaManualCode.Text='ab12';Save-CaptchaManualResult $false
  if((Get-Content -LiteralPath $label -Raw).Trim() -cne 'AB12'){throw 'Manual annotation was not saved uppercase'}
  $review2=New-Object TGLL.CaptchaReview;$review2.Key='fixture-2';$review2.SamplePath=$sample;$review2.CanSubmit=$true
  Show-CaptchaReviewSnapshot $review2 'fixture'
  if($script:captchaReviewKey -ne 'fixture-1' -or $captchaSaveAndSend.Enabled){throw 'Typing silently switched to another live image'}
  $captchaManualCode.Text='!';Save-CaptchaManualResult $false
  if((Get-Content -LiteralPath $label -Raw).Trim() -cne 'AB12'){throw 'Invalid label overwrote saved result'}
  Show-CaptchaReviewSnapshot $review2 'fixture' $true
  if($script:captchaReviewKey -ne 'fixture-2' -or $captchaManualCode.Text){throw 'Reload did not reset manual entry'}
  $script:captchaReviewPinned=$true;Show-CaptchaReviewSnapshot $review 'fixture'
  if($script:captchaReviewKey -ne 'fixture-2'){throw 'Archived preview was replaced automatically'}
  Write-Output 'PASS: manual image preview, uppercase label save, frozen typing, stale-image send guard, explicit reload and pinned archived image.'
 }finally{Show-CaptchaReviewSnapshot $null '' $true;Remove-Item -LiteralPath $sample,$label -Force -ErrorAction SilentlyContinue}
 Write-Output 'PASS: captcha tab, global auto preference, no installer in synthetic UI tests, strict owned Python updater name.'
}finally{$captchaAuto.Checked=$ocrPref;$script:settings.captchaAutoEnabled=$ocrSetting;Apply-CaptchaOptions}
