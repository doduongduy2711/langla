# Real job snapshots; all process checks simulated. Never launch or close a game.
$trSavedJobs=$script:accountJobs;$trFunctions=@{}
foreach($name in @('Get-SonCapMemberJob','Test-SonCapMemberRunning','Get-IzanamiMemberJob','Test-IzanamiMemberRunning','Test-AccountOpenBlocked')){$trFunctions[$name]=(Get-Item ('Function:\'+$name)).ScriptBlock}
$trCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0]
$trCloseField=[TGLL.NativeJob].GetField('ActivityTimeoutCloseOperation',[Reflection.BindingFlags]'NonPublic,Instance')
try{
 $script:accountJobs=@{};$script:trLive=@{'peer'=$true;'closed'=$false}
 function Test-AccountOpenBlocked($a){return [bool]$script:trLive[$a.id]}
 foreach($kind in @('SonCap','Izanami')){
  $script:trClosed=$trCtor.Invoke(@('synthetic.exe','closed','unused',$null,$true));$script:trPeer=$trCtor.Invoke(@('synthetic.exe','peer','unused',$null,$true))
  $script:trClosed.ActivityTimeoutClosed=$true;$script:trClosed.CloseRequested=$true;$script:trClosed.CancelRequested=$true;$script:trClosed.State='closed'
  $script:trPeer.State='opened';$script:trPeer.ProcessId=123456;$script:trPeer.ZoneReadAt=[datetime]::UtcNow;$script:trPeer.SonCapEnabled=$true;$script:trPeer.IzanamiEnabled=$true
  $script:trPeer.MapId=$(if($kind -eq 'SonCap'){97}else{89})
  Set-Item ('Function:\Get-'+$kind+'MemberJob') {param($m)if(!$m.job){return $null};if($m.id -eq 'closed'){return $script:trClosed};return $script:trPeer}
  Set-Item ('Function:\Test-'+$kind+'MemberRunning') {param($m)return [bool]$script:trLive[$m.id]}
  $index=@{'closed'=[pscustomobject]@{id='closed'};'peer'=[pscustomobject]@{id='peer'}}
  $m=[pscustomobject]@{id='closed';job=$script:trClosed.Id;queueExited=$true};$p=[pscustomobject]@{id='peer';job=$script:trPeer.Id}
  $b=[pscustomobject]@{state='blocked';members=@($m,$p);closeOperation=$null;error=''}
  if(!(Test-ActivityTeamStillRunning $kind $b 'closed')){throw "$kind did not recognize living activity peer"}
  $op=New-Object TGLL.CloseBatchOperation;$op.Completed=$false;$trCloseField.SetValue($script:trClosed,$op)
  Sync-ActivityCapacity $kind $b $index
  if(Test-ActivityMemberYielded $m){throw "$kind reopened before timeout close worker finished"}
  $op.Completed=$true;$script:trLive['closed']=$true;Sync-ActivityCapacity $kind $b $index
  if(Test-ActivityMemberYielded $m){throw "$kind reopened before old process exited"}
  $script:trLive['closed']=$false;Sync-ActivityCapacity $kind $b $index
  if(!(Test-ActivityMemberYielded $m) -or (Test-ActivityMemberExited $m) -or !$m.timeoutTeamRecovery -or $b.state -ne 'launching'){throw "$kind lost timeout member while peers still inside"}
  if((Restore-ActivityReservations @($b) 0) -ne 0 -or !(Test-ActivityMemberYielded $m)){throw "$kind bypassed max capacity during recovery"}
  $script:trPeer.ZoneReadAt=[datetime]::UtcNow.AddSeconds(-10)
  if((Restore-ActivityReservations @($b) 1) -ne 0){throw "$kind did not recheck peer before reserving recovery slot"}
  $script:trPeer.ZoneReadAt=[datetime]::UtcNow
  if((Restore-ActivityReservations @($b) 1) -ne 1 -or (Test-ActivityMemberYielded $m)){throw "$kind did not reserve exactly one recovery slot"}
  # When the team finishes during the wait, retire recovery rather than hanging the whole stage.
  $m.slotYielded=$true;$m.job='';$script:trPeer.SonCapFinished=$true;$script:trPeer.IzanamiNoAttempts=$true
  Sync-ActivityCapacity $kind $b $index
  if(!(Test-ActivityMemberExited $m) -or (Test-ActivityMemberYielded $m)){throw "$kind kept retrying after its team finished"}
  # Regression: a crashed game can retain izanami_running/opened after its lifetime worker exits.
  $script:trClosed=$trCtor.Invoke(@('synthetic.exe','closed','unused',$null,$true))
  $script:trClosed.State='izanami_running';$script:trClosed.ProcessId=999999
  $script:trPeer.State='opened';$script:trPeer.SonCapFinished=$false;$script:trPeer.IzanamiNoAttempts=$false;$script:trPeer.ZoneReadAt=[datetime]::UtcNow
  $m=[pscustomobject]@{id='closed';job=$script:trClosed.Id;queueExited=$true};$p=[pscustomobject]@{id='peer';job=$script:trPeer.Id}
  $b=[pscustomobject]@{state='blocked';members=@($m,$p);closeOperation=$null;error=''}
  if(!(Test-ActivityTeamRecoveryCandidate $kind $script:trClosed)){throw "$kind missed unrequested process exit"}
  Sync-ActivityCapacity $kind $b $index
  if(!(Test-ActivityMemberYielded $m) -or (Test-ActivityMemberExited $m) -or !$m.timeoutTeamRecovery){throw "$kind did not recover crashed member with living team"}
  if((58+(Get-ActivityReservedCount $b)) -ne 59){throw "$kind did not release crashed slot at 59/60"}
  if((Restore-ActivityReservations @($b) 0) -ne 0){throw "$kind crash recovery exceeded limit"}
  if((Restore-ActivityReservations @($b) 1) -ne 1 -or (58+(Get-ActivityReservedCount $b)) -ne 60){throw "$kind did not fill only missing team slot at 60/60"}
  if((Restore-ActivityReservations @($b) 1) -ne 0){throw "$kind reserved crashed member twice"}
  foreach($case in @('manual_close','cancelled','reconnecting','pending','finished')){
   $candidate=$trCtor.Invoke(@('synthetic.exe','closed','unused',$null,$true));$candidate.State='izanami_running';$candidate.ProcessId=999999
   switch($case){'manual_close'{$candidate.CloseRequested=$true} 'cancelled'{$candidate.CancelRequested=$true} 'reconnecting'{$candidate.ReconnectRequested=$true} 'pending'{$candidate.State='selecting_character'} 'finished'{$candidate.SonCapFinished=$true;$candidate.IzanamiNoAttempts=$true}}
   if(Test-ActivityTeamRecoveryCandidate $kind $candidate){throw "$kind reopened $case session"}
  }
  # Stale, village, finished, disconnected and closed peers cannot cause reopen loops.
  foreach($case in @('stale','village','finished','reconnecting','closed')){
   $script:trPeer.State='opened';$script:trPeer.ZoneReadAt=[datetime]::UtcNow;$script:trPeer.MapId=$(if($kind -eq 'SonCap'){97}else{89});$script:trPeer.SonCapFinished=$false;$script:trPeer.IzanamiNoAttempts=$false
   switch($case){'stale'{$script:trPeer.ZoneReadAt=[datetime]::UtcNow.AddSeconds(-10)} 'village'{$script:trPeer.MapId=59} 'finished'{$script:trPeer.SonCapFinished=$true;$script:trPeer.IzanamiNoAttempts=$true} 'reconnecting'{$script:trPeer.State='reconnecting'} 'closed'{$script:trPeer.State='closed'}}
   if(Test-ActivityTeamStillRunning $kind $b 'closed'){throw "$kind accepted $case peer as active"}
  }
 }
 Write-Output 'PASS: timeout and unexpectedly exited members return to its own queue only with fresh living SonCap/Izanami peers inside; old process/close-worker exit gates, max limit and stale/village/finished/reconnecting peer exclusions. No real game action.'
}finally{
 foreach($name in $trFunctions.Keys){Set-Item ('Function:\'+$name) $trFunctions[$name]};$script:accountJobs=$trSavedJobs
}
