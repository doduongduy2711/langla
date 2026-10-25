# Exercise production SonCap/Izanami schedulers; no process or game is launched.
$tpSaved=@{};$tpFns=@{}
foreach($name in @('accounts','accountJobs','soncapBatches','izanamiBatches','soncapQueuePaused','izanamiQueuePaused')){$tpSaved[$name]=Get-Variable -Scope Script -Name $name -ValueOnly}
foreach($name in @('Get-AccountJob','Test-AccountOpenBlocked','Sync-SonCapTeams','Sync-IzanamiTeams','Get-SonCapMemberJob','Get-IzanamiMemberJob','Test-SonCapMemberRunning','Test-IzanamiMemberRunning','Start-SonCapMember','Start-IzanamiMember')){$tpFns[$name]=(Get-Item ('Function:\'+$name)).ScriptBlock}
$tpLimits=@($soncapMax.Value,$izanamiMax.Value,$launchConcurrency.Value,$soncapGroupSize.Value,$izanamiGroupSize.Value);$tpCtor=[TGLL.NativeJob].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0]
try{
 $soncapGroupSize.Value=3;$izanamiGroupSize.Value=3
 function Get-AccountJob($id){return $script:tpJobs[$id]}
 function Test-AccountOpenBlocked($a){return [bool]$script:tpLive[$a.id] -or ($script:accountJobs.ContainsKey([string]$a.id) -and $script:tpJobs[$script:accountJobs[[string]$a.id]].State -eq 'queued')}
 function Sync-SonCapTeams {}
 function Sync-IzanamiTeams {}
 foreach($kind in @('SonCap','Izanami')){
  $k=$kind.ToLowerInvariant();$script:tpJobs=@{};$script:tpLive=@{'peer-a'=$true;'peer-b'=$true;'other-a'=$true;'other-b'=$true;'other-c'=$true};$script:tpStarts=@();$script:accountJobs=@{}
  $script:accounts=@(foreach($id in @('ordinary','new-1','new-2','new-3','peer-a','peer-b','other-a','other-b','other-c','failed')){[pscustomobject]@{id=$id;username=$id;soncap=($kind -eq 'SonCap');izanami=($kind -eq 'Izanami')}})
  foreach($id in @('peer-a','peer-b','other-a','other-b','other-c','failed')){$j=$tpCtor.Invoke(@('synthetic.exe',$id,'unused',$null,$true));$j.State=$(if($id -eq 'failed'){'failed'}else{'opened'});$j.ProcessId=$(if($id -eq 'failed'){0}else{123456});$j.MapId=59;$script:tpJobs[$id]=$j;$script:accountJobs[$id]=$id}
  $ordinary=[pscustomobject]@{id='ordinary';job='';slotYielded=$true;retryAt=[datetime]::UtcNow.AddSeconds(-1)}
  $oldRetry=[pscustomobject]@{id='failed';job='failed'}
  $normalBatch=[pscustomobject]@{state='launching';members=@($ordinary);error='';closeOperation=$null}
  $newBatch=[pscustomobject]@{state='waiting';members=@(1..3 | ForEach-Object {[pscustomobject]@{id=('new-'+$_);job=''}});error='';closeOperation=$null}
  $oldBatch=[pscustomobject]@{state='running';members=@([pscustomobject]@{id='peer-a';job='peer-a'},[pscustomobject]@{id='peer-b';job='peer-b'},$oldRetry);error='';closeOperation=$null}
  $otherBatch=[pscustomobject]@{state='running';members=@(foreach($id in @('other-a','other-b','other-c')){[pscustomobject]@{id=$id;job=$id}});error='';closeOperation=$null}
  Set-Variable -Scope Script -Name ($k+'Batches') -Value @($normalBatch,$newBatch,$otherBatch,$oldBatch);Set-Variable -Scope Script -Name ($k+'QueuePaused') -Value $false
  Set-Item ('Function:\Get-'+$kind+'MemberJob') {param($m)if($m.job){return $script:tpJobs[$m.job]};return $null}
  Set-Item ('Function:\Test-'+$kind+'MemberRunning') {param($m)return [bool]$script:tpLive[$m.id]}
  Set-Item ('Function:\Start-'+$kind+'Member') {param($m)$script:tpStarts+=@($m.id);$m.job='fresh-'+$m.id;$script:accountJobs[$m.id]=$m.job;$script:tpJobs[$m.job]=[pscustomobject]@{State='queued';ProcessId=0;CloseRequested=$false;CancelRequested=$false;GameSettingsReady=$false}}
  (Get-Variable -Name ($k+'Max') -ValueOnly).Value=6;$launchConcurrency.Value=1
  & ('Pump-'+$kind+'Queue')
  if(!$oldRetry.teamRetryPriority -or !(Test-ActivityMemberYielded $oldRetry) -or $script:tpStarts.Count -or !(Test-ActivityMemberYielded $ordinary) -or $newBatch.state -ne 'waiting'){throw "$kind let ordinary retry/new team take active team's cooldown slot"}
  if((Get-ActivityPriorityHoldCount @($normalBatch,$newBatch,$oldBatch)) -ne 1){throw "$kind lost held team recovery slot"}
  # A normal submission already reserved earlier must still come after recovery in the launch budget.
  (Get-Variable -Name ($k+'Max') -ValueOnly).Value=9;$ordinary.slotYielded=$false;$oldRetry.retryAt=[datetime]::UtcNow.AddSeconds(-1)
  & ('Pump-'+$kind+'Queue')
  if($script:tpStarts.Count -ne 1 -or $script:tpStarts[0] -ne 'failed' -or (Test-ActivityMemberYielded $oldRetry)){throw "$kind did not submit late active-team retry first"}
  & ('Pump-'+$kind+'Queue')
  if($script:tpStarts.Count -ne 2 -or $script:tpStarts[1] -ne 'ordinary' -or @($script:tpStarts | Where-Object {$_ -eq 'failed'}).Count -ne 1){throw "$kind duplicated recovery or starved ordinary work after recovery submission"}
  if($newBatch.state -ne 'waiting'){throw "$kind bypassed maximum for a new team"}
  $oldRetry.job='';$oldRetry.slotYielded=$true
  foreach($id in @('peer-a','peer-b')){$script:tpLive[$id]=$false;$script:tpJobs[$id].State='closed'}
  if((Get-ActivityPriorityHoldCount @($oldBatch)) -ne 0){throw "$kind held priority capacity after the team exited"}
 }
 Write-Output 'PASS: SonCap/Izanami active teams reserve retry capacity during cooldown, outrank earlier ordinary submissions, preserve caps/no duplicates, release priority after peers exit and resume normal work. No game action.'
}finally{
 foreach($name in $tpFns.Keys){Set-Item ('Function:\'+$name) $tpFns[$name]};foreach($name in $tpSaved.Keys){Set-Variable -Scope Script -Name $name -Value $tpSaved[$name]}
 $soncapGroupSize.Value=$tpLimits[3];$izanamiGroupSize.Value=$tpLimits[4];$soncapMax.Value=$tpLimits[0];$izanamiMax.Value=$tpLimits[1];$launchConcurrency.Value=$tpLimits[2]
}
