# Closed native sessions and unfinished bookkeeping cannot hold Auto all forever.
$drFunctions=@{};$drJobs=$script:accountJobs
foreach($name in @('Get-AccountJob','Test-AccountOpenBlocked','Get-SonCapMemberJob','Test-SonCapMemberRunning','Get-IzanamiMemberJob','Test-IzanamiMemberRunning','Get-DungeonMemberJob','Test-DungeonMemberRunning','Get-QuestMemberJob','Test-QuestMemberRunning')){$drFunctions[$name]=(Get-Item ('Function:\'+$name)).ScriptBlock}
$drCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0]
try{
 $script:accountJobs=@{};$script:drSessions=@{};$script:drAlive=@{};$script:drPending=@{}
 function Get-AccountJob($id){return $script:drSessions[$id]}
 function Test-AccountOpenBlocked($a){return ([bool]$script:drAlive[$a.id] -or [bool]$script:drPending[$a.id])}
 foreach($kind in @('SonCap','Izanami','Dungeon','Quest')){
  Set-Item ('Function:\Get-'+$kind+'MemberJob') {param($m)if($m.job){return $script:drSessions[$m.job]};return $null}
  Set-Item ('Function:\Test-'+$kind+'MemberRunning') {param($m)return [bool]$script:drAlive[$m.id]}
  $j=$drCtor.Invoke(@('synthetic.exe','fixture','unused',$null,$true));$j.State='opened';$j.ProcessId=999999;$j.GameSettingsReady=$false;$j.TrainReady=$false;$script:drSessions[$j.Id]=$j
  $m=[pscustomobject]@{id='ghost';job=$j.Id};$index=@{'ghost'=[pscustomobject]@{id='ghost'}}
  $b=[pscustomobject]@{state='blocked';members=@($m);closeOperation=$null;error=''}
  $script:drAlive['ghost']=$true;Sync-ActivityCapacity $kind $b $index
  if($b.state -eq 'done'){throw "$kind dropped a real process"}
  $script:drAlive['ghost']=$false;$script:drPending['ghost']=$true;Sync-ActivityCapacity $kind $b $index
  if($b.state -eq 'done'){throw "$kind dropped pending reopen"}
  $script:drPending.Clear();Sync-ActivityCapacity $kind $b $index
  if($b.state -ne 'done'){throw "$kind kept State=opened after confirmed process exit"}
  $b=[pscustomobject]@{state='blocked';members=@($m,[pscustomobject]@{id='not-submitted';job=''});closeOperation=[pscustomobject]@{Completed=$false};error='';limitClose=$true}
  Complete-ActivityDrainedClose $kind $b $index;if($b.state -eq 'done'){throw "$kind ignored pending close worker"}
  $b.closeOperation.Completed=$true;Complete-ActivityDrainedClose $kind $b $index
  if($b.state -ne 'done'){throw "$kind kept unsubmitted reservation after whole-team close"}
 }
 foreach($kind in @('SonCap','Izanami')){
  $members=@();$index=@{}
  foreach($id in @('one','two')){
   $j=$drCtor.Invoke(@('synthetic.exe',$id,'unused',$null,$true));$j.State='closed';$j.ProcessId=999999;$j.CloseRequested=$true;$j.CancelRequested=$true;$j.ActivityTimeoutClosed=$true;$script:drSessions[$j.Id]=$j;$script:accountJobs[$id]=$j.Id
   $members+=@([pscustomobject]@{id=$id;job='';retiredJob=$j.Id;slotYielded=$true;queueExited=$false;timeoutTeamRecovery=$true;recoveryKind=$kind;retryAt=[datetime]::UtcNow});$index[$id]=[pscustomobject]@{id=$id}
  }
  $b=[pscustomobject]@{state='launching';members=$members;closeOperation=$null;error=''}
  Sync-ActivityCapacity $kind $b $index
  if($b.state -ne 'done' -or @($members | Where-Object {$_.slotYielded -or !$_.queueExited}).Count){throw "$kind circular timeout recovery kept dead team"}
 }
 Write-Output 'PASS: all four queues finish verified exited State=opened sessions and unsubmitted closed-team slots, retain live/pending/closing work, and resolve two mutually waiting SonCap/Izanami timeout recoveries. No game action.'
}finally{
 foreach($name in $drFunctions.Keys){Set-Item ('Function:\'+$name) $drFunctions[$name]};$script:accountJobs=$drJobs
}
