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
 Write-Output 'PASS: captcha tab, global auto preference, no installer in synthetic UI tests, strict owned Python updater name.'
}finally{$captchaAuto.Checked=$ocrPref;$script:settings.captchaAutoEnabled=$ocrSetting;Apply-CaptchaOptions}
