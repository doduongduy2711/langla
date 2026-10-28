$caOldAccounts=$script:accounts;$caOldJobs=$script:accountJobs;$caOldUiTest=$UiTest
$caBegin=(Get-Item Function:Begin-CloseAllOperation).ScriptBlock;$caWriter=(Get-Item Function:Write-JsonAtomic).ScriptBlock
try{
 $script:caStarts=0;$script:caWrites=0;$script:accountJobs=@{};$script:accounts=@(1..300 | ForEach-Object {[pscustomobject]@{id="close-fixture-$_";username="close-fixture-$_";soncap=$true;cuongThiEnabled=$true;cuongThiConfig=[pscustomobject]@{Move=$true;MapId=69;X=700;Y=400;Zone=14}}});Refresh-Grid
 function Write-JsonAtomic {param($path,$value);$script:caWrites++}
 function Begin-CloseAllOperation {$script:caStarts++;return [pscustomobject]@{Completed=$false;Error='';Result=[pscustomobject]@{Closed=300;Failed=0}}}
 $UiTest=$false;$caWatch=[Diagnostics.Stopwatch]::StartNew();Start-CloseAllUi
 if($caWatch.ElapsedMilliseconds -gt 1500){throw 'Close-all UI handler blocked despite background adapter'}
 if(!$script:closingAll -or $closeAllButton.Enabled -or $script:caWrites -ne 1){throw 'Busy state or single config save missing'}
 Start-CloseAllUi;if($script:caStarts -ne 1){throw 'Duplicate close-all began'}
 if(@($script:accounts | Where-Object {!$_.cuongThiEnabled -or !$_.cuongThiConfig.Move -or $_.cuongThiConfig.Zone -ne 14}).Count){throw 'Close-all cleared persistent Cuong Thi preference'}
 if(@($script:accounts | Where-Object {$_.soncap}).Count){throw 'Auto preference restarted while closing'}
 Complete-CloseAllUi;if(!$script:closingAll){throw 'Close-all UI finished before operation completed'}
 $script:closeAllOperation.Completed=$true;Complete-CloseAllUi
 if($script:closingAll -or !$closeAllButton.Enabled -or $closeAllButton.Text -ne 'Tắt tất cả' -or $status.Text -notmatch '300'){throw 'Close-all completion did not restore controls/result'}
 Write-Output 'PASS: close-all 300-row UI returns promptly, disables duplicates, one config save, pending completion, preference cleanup and button/result restoration; no real game closed.'
}finally{
 $UiTest=$caOldUiTest;$closeAllTimer.Stop();$script:closingAll=$false;$script:closeAllOperation=$null;$script:deferAccountSave=$false;$script:accountSavePending=$false
 Set-Item Function:Begin-CloseAllOperation $caBegin;Set-Item Function:Write-JsonAtomic $caWriter
 $script:accounts=$caOldAccounts;$script:accountJobs=$caOldJobs;$closeAllButton.Enabled=$true;$closeAllButton.Text='Tắt tất cả';Refresh-Grid
}
