# Exercise every production scheduler with dead launch attempts and a bounded pool.
$capSaved=@{};$capFuncs=@{}
foreach($name in @('accounts','accountJobs','soncapBatches','izanamiBatches','dungeonBatches','questBatches','soncapQueuePaused','izanamiQueuePaused','dungeonQueuePaused','questQueuePaused')){$capSaved[$name]=Get-Variable -Scope Script -Name $name -ValueOnly}
foreach($name in @('Get-AccountJob','Test-AccountOpenBlocked','Sync-SonCapTeams','Sync-IzanamiTeams')){$capFuncs[$name]=(Get-Item ('Function:\'+$name)).ScriptBlock}
foreach($kind in @('SonCap','Izanami','Dungeon','Quest')){foreach($verb in @('Get-','Test-','Start-')){$name=$verb+$kind+$(if($verb -eq 'Get-'){'MemberJob'}elseif($verb -eq 'Test-'){'MemberRunning'}else{'Member'});$capFuncs[$name]=(Get-Item ('Function:\'+$name)).ScriptBlock}}
$capControls=@($soncapMax,$izanamiMax,$dungeonMax,$questMax,$launchConcurrency);$capValues=@($capControls | ForEach-Object {$_.Value})
try{
 function Get-AccountJob($id){return $script:capJobs[$id]}
 function Test-AccountOpenBlocked($a){return [bool]$script:capOpen[$a.id]}
 function Sync-SonCapTeams {}
 function Sync-IzanamiTeams {}
 foreach($kind in @('SonCap','Izanami','Dungeon','Quest')){
  $k=$kind.ToLowerInvariant();$script:capJobs=@{};$script:capOpen=@{};$script:accountJobs=@{};$script:capStarts=0
  $script:accounts=@(1..12 | ForEach-Object {[pscustomobject]@{id=('cap-'+$_);username=('cap-'+$_);soncap=$false;izanami=$false;dungeonHigh=$false;autoQuest=$false}})
  $batches=@(foreach($a in $script:accounts){$id=[string]$a.id;$n=[int]$id.Substring(4);$job=''
   if($n -le 6){$job='dead-'+$id;$script:capJobs[$job]=[pscustomobject]@{Id=$job;State='failed';ProcessId=0;CloseRequested=$false;CancelRequested=$false;GameSettingsReady=$false};$script:accountJobs[$id]=$job}
   [pscustomobject]@{state=$(if($n -le 6){'running'}else{'waiting'});members=@([pscustomobject]@{id=$id;job=$job});error='';closeOperation=$null;limitClose=$false}
  })
  Set-Variable -Scope Script -Name ($k+'Batches') -Value $batches;Set-Variable -Scope Script -Name ($k+'QueuePaused') -Value $false
  (Get-Variable -Name ($k+'Max') -ValueOnly).Value=6;$launchConcurrency.Value=6
  Set-Item ('Function:\Get-'+$kind+'MemberJob') {param($m)if($m.job){return $script:capJobs[$m.job]};return $null}
  Set-Item ('Function:\Test-'+$kind+'MemberRunning') {param($m)return [bool]$script:capOpen[$m.id]}
  Set-Item ('Function:\Start-'+$kind+'Member') {param($m)$script:capStarts++;$m.job='live-'+$m.id;$script:accountJobs[$m.id]=$m.job;$script:capOpen[$m.id]=$true;$script:capJobs[$m.job]=[pscustomobject]@{Id=$m.job;State='selecting_character';ProcessId=0;CloseRequested=$false;CancelRequested=$false;GameSettingsReady=$false}}
  & ('Pump-'+$kind+'Queue')
  if($script:capStarts -ne 6 -or @($script:capJobs.Values | Where-Object {$_.State -eq 'failed' -and !$_.CancelRequested}).Count){throw "$kind failed launches still hold slots"}
  if(@($batches | Where-Object {$_.state -eq 'done'}).Count){throw "$kind marked retry as completed"}
  & ('Pump-'+$kind+'Queue');if($script:capStarts -ne 6){throw "$kind exceeded max while clients are logging in"}
  foreach($b in $batches[0..5]){$b.members[0].retryAt=[datetime]::UtcNow.AddSeconds(-1)}
  & ('Pump-'+$kind+'Queue');if($script:capStarts -ne 6){throw "$kind retry exceeded limit"}
  # Deliberate exit returns one slot; exactly one cooling-down account can retry.
  $script:capOpen['cap-7']=$false;$script:capJobs['live-cap-7'].State='closed';$script:capJobs['live-cap-7'].CloseRequested=$true
  & ('Pump-'+$kind+'Queue');if($script:capStarts -ne 7 -or $batches[6].state -ne 'done'){throw "$kind did not refill exactly one confirmed exit"}
  # A failed job whose process is still present must retain its slot.
  $j=$script:capJobs['live-cap-8'];$j.State='failed';$j.ProcessId=999999;$j.CancelRequested=$false
  & ('Pump-'+$kind+'Queue');if($script:capStarts -ne 7 -or $j.CancelRequested){throw "$kind released a still-present failed session"}
  # A partly submitted team must resume after earlier peers exit (live log: 109-114).
  $script:capJobs=@{};$script:capOpen=@{};$script:accountJobs=@{};$script:capStarts=0
  $script:accounts=@($script:accounts[0..5]);$members=@()
  foreach($a in $script:accounts[0..4]){
   $job='closed-'+$a.id;$script:capJobs[$job]=[pscustomobject]@{Id=$job;State='closed';ProcessId=0;CloseRequested=$true;CancelRequested=$true;GameSettingsReady=$false}
   $script:accountJobs[$a.id]=$job;$members+=@([pscustomobject]@{id=$a.id;job=$job})
  }
  $last=[pscustomobject]@{id=$script:accounts[5].id;job=''};$members+=@($last)
  $partial=[pscustomobject]@{state='blocked';members=$members;error='peer exited';closeOperation=$null;limitClose=$false}
  Set-Variable -Scope Script -Name ($k+'Batches') -Value @($partial)
  & ('Pump-'+$kind+'Queue')
  if($script:capStarts -ne 1 -or !$last.job -or $partial.state -eq 'done'){throw "$kind stranded or skipped unsubmitted member after peer exit"}
  & ('Pump-'+$kind+'Queue');if($script:capStarts -ne 1){throw "$kind duplicated resumed launch"}
  # Closing and cooling-down reservations must not be resumed by this repair.
  $last.job='';$partial.state='blocked';$partial.closeOperation=[pscustomobject]@{Completed=$false}
  $index=@{};foreach($a in $script:accounts){$index[$a.id]=$a}
  Sync-ActivityCapacity $kind $partial $index
  if($partial.state -ne 'blocked'){throw "$kind resumed team while closing"}
  $partial.closeOperation=$null;$last | Add-Member NoteProperty slotYielded $true -Force;$last | Add-Member NoteProperty retryAt ([datetime]::UtcNow.AddMinutes(1)) -Force
  Sync-ActivityCapacity $kind $partial $index
  if($partial.state -ne 'blocked'){throw "$kind bypassed retry cooldown"}
  # Synchronous launch failure also yields its reservation immediately.
  $m=[pscustomobject]@{id='cap-absent';job=''};$b=[pscustomobject]@{state='blocked';members=@($m);error='';closeOperation=$null}
  Handle-ActivityLaunchError $kind $b $m @{}
  if(!(Test-ActivityMemberYielded $m) -or (Get-ActivityReservedCount $b) -ne 0){throw "$kind synchronous failure kept a slot"}
 }
 # Mixed team: only failed member yields, living peers keep their own reservations.
 $team=[pscustomobject]@{members=@([pscustomobject]@{slotYielded=$true},[pscustomobject]@{queueExited=$true},[pscustomobject]@{job='pending'})}
 if((Get-ActivityReservedCount $team) -ne 1){throw 'Mixed team capacity still counts dead members'}
 Write-Output 'PASS: all four schedulers release failed launches, fill max6 with healthy/pending clients, bound retries, refill one actual exit, retain still-present failed sessions, preserve team peer reservations, resume unsubmitted members after peer exit without duplicates or bypassing close/cooldown gates, and yield synchronous errors. No real game action.'
}finally{
 foreach($name in $capFuncs.Keys){Set-Item ('Function:\'+$name) $capFuncs[$name]}
 foreach($name in $capSaved.Keys){Set-Variable -Scope Script -Name $name -Value $capSaved[$name]}
 for($n=0;$n -lt $capControls.Count;$n++){$capControls[$n].Value=$capValues[$n]}
}
