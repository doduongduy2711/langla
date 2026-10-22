$icOldJobs=$script:accountJobs;$icOldRecords=$script:izanamiCompletedClose;$icOldCursor=$script:izanamiCompletedCloseCursor;$icOldCheck=$izanamiCloseAfter.Checked
$icOldBegin=(Get-Item Function:Begin-IzanamiCompletedClose).ScriptBlock
$icJobs=[TGLL.NativeBackend].GetField('jobs',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
$icCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0];$icFixtures=@()
try{
 $script:accountJobs=@{};$script:izanamiCompletedClose=@{};$script:izanamiCompletedCloseCursor=0;$script:icCloseCalls=@()
 function Begin-IzanamiCompletedClose($job){$script:icCloseCalls+=@($job.Id);return [pscustomobject]@{Completed=$false;Failed=0;Error=''}}
 $j=$icCtor.Invoke(@('C:\synthetic.exe','completed-test','unused',$null,$true));$icFixtures+=@($j);$null=$icJobs.TryAdd($j.Id,$j)
 $script:accountJobs['completed']=$j.Id;$j.State='opened';$j.ProcessId=123456;$j.IzanamiEnabled=$true;$j.IzanamiRoundComplete=$true;$j.IzanamiRoundsCompleted=1;$j.IzanamiRoundsTarget=1;$j.MapId=59;$j.ZoneReadAt=[datetime]::UtcNow
 $izanamiCloseAfter.Checked=$false;Sync-IzanamiCompletedClose;if($script:icCloseCalls.Count){throw 'Unchecked close preference ignored'}
 $izanamiCloseAfter.Checked=$true
 foreach($flag in @('UpRequested','FarmEnabled','DailyManaged','QuestEnabled','SonCapEnabled','IzanamiQueueOnly','CloseRequested','CancelRequested','ReconnectRequested')){
  $j.$flag=$true;Sync-IzanamiCompletedClose;if($script:icCloseCalls.Count){throw ('Protected session closed: '+$flag)};$j.$flag=$false
 }
 $j.IzanamiRoundsTarget=4;Sync-IzanamiCompletedClose;if($script:icCloseCalls.Count){throw 'Closed before configured rounds completed'};$j.IzanamiRoundsTarget=1
 $j.MapId=89;Sync-IzanamiCompletedClose;if($script:icCloseCalls.Count){throw 'Closed while still inside instance'};$j.MapId=59
 $j.ZoneReadAt=[datetime]::UtcNow.AddSeconds(-10);Sync-IzanamiCompletedClose;if($script:icCloseCalls.Count){throw 'Stale outside-map snapshot closed session'};$j.ZoneReadAt=[datetime]::UtcNow
 Sync-IzanamiCompletedClose;Sync-IzanamiCompletedClose
 if($script:icCloseCalls.Count -ne 1 -or !$script:izanamiCompletedClose.ContainsKey($j.Id)){throw 'Direct-open completed Izanami missing close or duplicated pending close'}
 # Run after queue reset/paused: no queue batch must be needed for direct-open completion.
 $script:icCloseCalls=@();$script:accountJobs=@{};$script:izanamiCompletedClose=@{};$script:izanamiCompletedCloseCursor=0
 for($n=0;$n -lt 49;$n++){
  $x=$icCtor.Invoke(@('C:\synthetic.exe',('complete-'+$n),'unused',$null,$true));$icFixtures+=@($x);$null=$icJobs.TryAdd($x.Id,$x)
  $x.State='opened';$x.ProcessId=200000+$n;$x.IzanamiEnabled=$true;$x.IzanamiRoundComplete=$true;$x.IzanamiRoundsCompleted=1;$x.IzanamiRoundsTarget=1;$x.MapId=59;$x.ZoneReadAt=[datetime]::UtcNow;$script:accountJobs['complete-'+$n]=$x.Id
 }
 Sync-IzanamiCompletedClose;if($script:icCloseCalls.Count -ne 24){throw 'Completed account close scan is not bounded'}
 Sync-IzanamiCompletedClose;Sync-IzanamiCompletedClose
 if($script:icCloseCalls.Count -ne 49){throw 'Completed account close scan skipped or duplicated clients'}
 Write-Output 'PASS: direct-open Izanami closes after configured rounds and fresh return; checkbox, Up/Farm/daily/quest/SonCap/queue/closing protections, no duplicate close, and 49-account bounded scan. No real game closed.'
}finally{
 foreach($x in $icFixtures){$ignored=$null;$null=$icJobs.TryRemove($x.Id,[ref]$ignored)}
 Set-Item Function:Begin-IzanamiCompletedClose $icOldBegin;$script:accountJobs=$icOldJobs;$script:izanamiCompletedClose=$icOldRecords;$script:izanamiCompletedCloseCursor=$icOldCursor;$izanamiCloseAfter.Checked=$icOldCheck
}
