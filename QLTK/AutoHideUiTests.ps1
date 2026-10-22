Initialize-AutoHideUi
$hideOriginal=(Get-Item Function:Set-LoggedInGameHidden).ScriptBlock
$hideSavedChecked=$autoHideAfterLogin.Checked;$hideSettingPresent=$script:settings.ContainsKey('autoHideAfterLogin');$hideSetting=$script:settings.autoHideAfterLogin
$hideSavedJobs=$script:accountJobs;$hideSavedDone=$script:autoHideDone;$hideSavedRetry=$script:autoHideRetry;$hideSavedCursor=$script:autoHideCursor
$hideJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$hideCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0];$hideFixtures=@()
try{
    $script:hideCalls=@();$script:hideSuccess=$true
    function Set-LoggedInGameHidden([int]$gamePid){$script:hideCalls+=@($gamePid);return $script:hideSuccess}
    $script:autoHideDone=@{};$script:autoHideRetry=@{};$script:autoHideCursor=0;$script:accountJobs=@{}
    $autoHideAfterLogin.Checked=$true;Save-AutoHidePreference
    if(!$script:settings.autoHideAfterLogin -or $autoHideAfterLogin.Parent -ne $options -or $autoHideAfterLogin.Bounds.Right -gt $options.ClientSize.Width){throw 'Auto hide checkbox/preference/layout failed'}
    $hideJob=$hideCtor.Invoke(@('C:\synthetic.exe','hide-test','unused',$null,$true));$hideJob.ProcessId=123456;$hideFixtures+=@($hideJob)
    $null=$hideJobs.TryAdd($hideJob.Id,$hideJob);$script:accountJobs['hide-test']=$hideJob.Id
    $hideJob.TrainReady=$true;$hideJob.GameSettingsReady=$true;$now=[datetime]::UtcNow
    foreach($st in @('queued','starting','typing','submitted','selecting_character','character_submitted','waiting_map','waiting_popup','confirming_popup','retrying','reconnecting','manual_entry','startup_unavailable','failed','closed','cancelled')){$hideJob.State=$st;Apply-AutoHideJob 'hide-test' $hideJob $now}
    if($script:hideCalls.Count){throw 'Game hidden before successful login'}
    $hideJob.State='opened';$hideJob.GameSettingsReady=$false;Apply-AutoHideJob 'hide-test' $hideJob $now
    if($script:hideCalls.Count){throw 'Unready game hidden'}
    $hideJob.GameSettingsReady=$true;$script:hideSuccess=$false;Apply-AutoHideJob 'hide-test' $hideJob $now;Apply-AutoHideJob 'hide-test' $hideJob $now.AddSeconds(1)
    if($script:hideCalls.Count -ne 1 -or $script:autoHideDone.Count){throw 'Missing window marked hidden or retried too quickly'}
    $script:hideSuccess=$true;Apply-AutoHideJob 'hide-test' $hideJob $now.AddSeconds(3);Apply-AutoHideJob 'hide-test' $hideJob $now.AddSeconds(5)
    if($script:hideCalls.Count -ne 2 -or $script:autoHideDone.Count -ne 1){throw 'Hide once/manual Show retention failed'}
    $hideJob.ProcessId=123457;Apply-AutoHideJob 'hide-test' $hideJob $now.AddSeconds(6)
    if($script:hideCalls.Count -ne 3){throw 'Reopened game not hidden'}
    $autoHideAfterLogin.Checked=$false;$hideJob.ProcessId=123458;Apply-AutoHideJob 'hide-test' $hideJob $now.AddSeconds(9)
    if($script:hideCalls.Count -ne 3 -or $script:settings.autoHideAfterLogin){throw 'Unticking auto hide did not disable/save'}
    $autoHideAfterLogin.Checked=$true;$script:accountJobs=@{};$script:autoHideDone=@{};$script:autoHideRetry=@{};$script:hideCalls=@()
    for($n=0;$n -lt 49;$n++){
        $j=$hideCtor.Invoke(@('C:\synthetic.exe',('hide-batch-'+$n),'unused',$null,$true));$j.State='opened';$j.ProcessId=200000+$n;$j.TrainReady=$true;$j.GameSettingsReady=$true;$hideFixtures+=@($j)
        $null=$hideJobs.TryAdd($j.Id,$j);$script:accountJobs['hide-'+$n]=$j.Id
    }
    Invoke-AutoHideAfterLogin;if($script:hideCalls.Count -ne 24){throw 'Auto hide work not bounded per timer tick'}
    Invoke-AutoHideAfterLogin;Invoke-AutoHideAfterLogin;Invoke-AutoHideAfterLogin
    if($script:hideCalls.Count -ne 49){throw 'Auto hide scan skipped/repeated clients'}
    Write-Output 'PASS: saved auto-hide checkbox, login success gate, disabled/startup/closed exclusions, bounded retries, one hide per session, manual Show retained, new PID/relogin and 49-client bounded batches.'
}finally{
    foreach($j in $hideFixtures){$ignored=$null;$null=$hideJobs.TryRemove($j.Id,[ref]$ignored)}
    Set-Item Function:Set-LoggedInGameHidden $hideOriginal
    $autoHideAfterLogin.Checked=$hideSavedChecked
    if($hideSettingPresent){$script:settings.autoHideAfterLogin=$hideSetting}else{$script:settings.Remove('autoHideAfterLogin')}
    $script:accountJobs=$hideSavedJobs;$script:autoHideDone=$hideSavedDone;$script:autoHideRetry=$hideSavedRetry;$script:autoHideCursor=$hideSavedCursor
}
